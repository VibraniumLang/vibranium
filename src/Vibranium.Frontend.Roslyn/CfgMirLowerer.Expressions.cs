using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Vibranium.Compiler.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class CfgMirLowerer
{
    private MirOperand LowerConditionOperand(IOperation condOp, VibraniumSpan span)
    {
        if (condOp is IIsNullOperation && condOp.Syntax.AncestorsAndSelf().Any(node =>
            node is ConditionalAccessExpressionSyntax || node.IsKind(SyntaxKind.CoalesceExpression) ||
            node.IsKind(SyntaxKind.CoalesceAssignmentExpression)))
        {
            // The gate reports the null operator. Preserve both CFG successors and other
            // diagnostics without an additional VIB000 for Roslyn's synthetic IsNull node.
            int temp = CreateTempLocal(MirType.Bool, span);
            _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(temp),
                new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(false), MirType.Bool, span)), span));
            return new MirOperand.CopyOrMove(MirPlace.Local(temp), MirType.Bool, span);
        }

        if (condOp is IIsPatternOperation isPat)
        {
            return LowerIsPatternExpression(isPat, span);
        }

        if (condOp is IBinaryOperation bin &&
            TryExtractSwitchCaseNaNScrutinee(bin, out var nanScrutOp, out var nanScrutType))
        {
            var scrutOperand = LowerOperand(nanScrutOp, nanScrutType);
            if (TryGetStrictConstantOperand(nanScrutOp, nanScrutType, span, out var constantOperand))
            {
                scrutOperand = constantOperand;
            }
            return EmitIsNaNTest(scrutOperand, nanScrutType, span);
        }

        if (condOp is IBinaryOperation comparison &&
            comparison.OperatorKind is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals or
                BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual or
                BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual)
        {
            var operandType = MapMirType(SubstituteType(comparison.LeftOperand.Type));
            if (TryGetStrictConstantOperand(comparison.LeftOperand, operandType, span, out var left) &&
                TryGetStrictConstantOperand(comparison.RightOperand, operandType, span, out var right) &&
                TryFoldPatternComparison(left, right, operandType, MapBinaryOp(comparison.OperatorKind), span, out var folded))
            {
                return folded;
            }
        }

        return LowerOperand(condOp, MirType.Bool);
    }

    // Trace only single-definition CFG captures, never values assigned to runtime locals.
    // The original capture evaluation remains in its defining block.
    private bool TryGetStrictConstantOperand(IOperation operation, MirType type, VibraniumSpan span, out MirOperand operand)
    {
        operation = UnwrapImplicitIdentityConversion(operation);
        while (operation is IFlowCaptureReferenceOperation capture &&
            _captures.TryGetValue(capture.Id, out var info) &&
            (info.StrictConstantSource ?? info.SingleValueOp) is { } source)
        {
            operation = UnwrapImplicitIdentityConversion(source);
        }
        if (operation.ConstantValue.HasValue)
        {
            return TryMakeConstantOperand(operation.ConstantValue.Value, operation.Type, type, span, out operand);
        }
        operand = null!;
        return false;
    }

    private static bool TryFoldPatternComparison(MirOperand left, MirOperand right, MirType type,
        MirBinaryOp operation, VibraniumSpan span, out MirOperand result)
    {
        result = null!;
        if (left is not MirOperand.Const lhs || right is not MirOperand.Const rhs)
        {
            return false;
        }
        int comparison;
        bool unordered = false;
        switch (lhs.Value, rhs.Value)
        {
            case (MirConstant.BoolConst a, MirConstant.BoolConst b):
                comparison = a.Value.CompareTo(b.Value);
                break;
            case (MirConstant.Int32Const a, MirConstant.Int32Const b):
                comparison = MirType.IsUnsignedInteger(type)
                    ? unchecked((uint)a.Value).CompareTo(unchecked((uint)b.Value))
                    : a.Value.CompareTo(b.Value);
                break;
            case (MirConstant.Int64Const a, MirConstant.Int64Const b):
                comparison = MirType.IsUnsignedInteger(type)
                    ? unchecked((ulong)a.Value).CompareTo(unchecked((ulong)b.Value))
                    : a.Value.CompareTo(b.Value);
                break;
            case (MirConstant.Float32Const a, MirConstant.Float32Const b):
                unordered = float.IsNaN(a.Value) || float.IsNaN(b.Value);
                comparison = a.Value.CompareTo(b.Value);
                break;
            case (MirConstant.Float64Const a, MirConstant.Float64Const b):
                unordered = double.IsNaN(a.Value) || double.IsNaN(b.Value);
                comparison = a.Value.CompareTo(b.Value);
                break;
            default:
                return false;
        }
        bool value = operation switch
        {
            MirBinaryOp.Eq => !unordered && comparison == 0,
            MirBinaryOp.Ne => unordered || comparison != 0,
            MirBinaryOp.Lt => !unordered && comparison < 0,
            MirBinaryOp.Le => !unordered && comparison <= 0,
            MirBinaryOp.Gt => !unordered && comparison > 0,
            MirBinaryOp.Ge => !unordered && comparison >= 0,
            _ => false,
        };
        result = new MirOperand.Const(new MirConstant.BoolConst(value), MirType.Bool, span);
        return true;
    }

    private MirOperand LowerIsPatternExpression(IIsPatternOperation isPat, VibraniumSpan span)
    {
        if (RejectUnsupportedPattern(isPat.Pattern))
        {
            return new MirOperand.Const(new MirConstant.BoolConst(RejectedPatternResult(isPat.Pattern)), MirType.Bool, span);
        }

        var scrutTypeSym = SubstituteType(isPat.Value.Type);
        var scrutMirType = MapMirType(scrutTypeSym);

        bool invert = false;
        IPatternOperation innerPattern = isPat.Pattern;
        while (innerPattern is INegatedPatternOperation negPat)
        {
            invert = !invert;
            innerPattern = negPat.Pattern;
        }

        if (scrutMirType is MirType.OptionType or MirType.OptionRefType or MirType.OptionMutType)
        {
            MirType elemType = scrutMirType switch
            {
                MirType.OptionType o => o.Inner,
                MirType.OptionRefType orf => orf.Inner,
                MirType.OptionMutType om => om.Inner,
                _ => MirType.Int32,
            };

            if (TryExtractOptionArm(innerPattern, elemType, span, out bool isSomeArm, out int someBindingId, out var wrapperLocalSym, out var extraSubpattern))
            {
                var scrutinee = LowerMatchScrutinee(isPat.Value, scrutMirType);
                if (_module.IsLinearResource(scrutMirType))
                {
                    if (scrutinee is MirMatchScrutinee.PlaceScrutinee)
                    {
                        EmitDiag(
                            VibraniumDiagnosticIds.LinearResourceViolation,
                            "Cannot borrow-match linear Option<T> in place; consume it via '.IsNone(out ...)', '.Unwrap()', or 'Move(...)'.",
                            span);
                    }
                    if (extraSubpattern is not null)
                    {
                        EmitDiag(
                            VibraniumDiagnosticIds.LinearResourceViolation,
                            "Nested subpatterns inside linear Option<T> are banned in Vibranium.",
                            span);
                    }
                }

                int resLocalId = CreateTempLocal(MirType.Bool, span, "__is_pat");
                int matchBlk = _nextSyntheticBlockId++;
                int failBlk = _nextSyntheticBlockId++;
                int mergeBlk = _nextSyntheticBlockId++;
                _currentOrderedBlockIds.Add(matchBlk);
                _currentOrderedBlockIds.Add(failBlk);
                _currentOrderedBlockIds.Add(mergeBlk);

                var matchStmts = new List<MirStatement>();
                if (wrapperLocalSym is not null && !isSomeArm)
                {
                    matchStmts.Add(NoneDesignationAssign(wrapperLocalSym, span, true));
                }
                else if (wrapperLocalSym is not null)
                {
                    int wId = GetOrCreateLocalId(wrapperLocalSym, isPatternBindingOverride: true);
                    matchStmts.Add(new MirStatement.Assign(
                        MirPlace.Local(wId),
                        new MirRvalue.MakeSomeWrapper(elemType, new MirOperand.CopyOrMove(MirPlace.Local(someBindingId), elemType, span)),
                        span));
                }
                matchStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(resLocalId),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(!invert), MirType.Bool, span)),
                    span));
                _mirBlockStmts[matchBlk] = matchStmts;
                _mirBlockTerms[matchBlk] = new MirTerminator.Goto(mergeBlk, span);

                _mirBlockStmts[failBlk] = new List<MirStatement>
                {
                    new MirStatement.Assign(
                        MirPlace.Local(resLocalId),
                        new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(invert), MirType.Bool, span)),
                        span),
                };
                _mirBlockTerms[failBlk] = new MirTerminator.Goto(mergeBlk, span);

                int someTarget = isSomeArm ? matchBlk : failBlk;
                int noneTarget = isSomeArm ? failBlk : matchBlk;
                if (extraSubpattern is not null)
                {
                    bool consumedByVal = scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: false };
                    someTarget = BuildExtraSubpatternCheckBlock(someBindingId, elemType, consumedByVal, extraSubpattern, someTarget, noneTarget, span, _currentOrderedBlockIds);
                }

                _mirBlockStmts[_currentBlockId] = _currentStmts;
                _mirBlockTerms[_currentBlockId] = new MirTerminator.MatchOption(scrutinee, someBindingId, someTarget, noneTarget, span);
                _currentBlockId = mergeBlk;
                _currentStmts = new List<MirStatement>();
                return new MirOperand.CopyOrMove(MirPlace.Local(resLocalId), MirType.Bool, span);
            }
        }
        else if (scrutMirType is MirType.ResultType resType &&
                 TryExtractResultArm(innerPattern, resType, span, out bool isOkArm, out int bindingId, out var resWrapperSym, out var resExtraSub))
        {
            var scrutinee = LowerMatchScrutinee(isPat.Value, resType);
            if (_module.IsLinearResource(resType))
            {
                if (scrutinee is MirMatchScrutinee.PlaceScrutinee)
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        "Cannot borrow-match linear Result<T, E> in place; consume it via '.IsErr(out ..., out ...)', '.Unwrap()', or 'Move(...)'.",
                        span);
                }
                if (resExtraSub is not null)
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        "Nested subpatterns inside linear Result<T, E> are banned in Vibranium.",
                        span);
                }
            }

            int resLocalId = CreateTempLocal(MirType.Bool, span, "__is_pat");
            int matchBlk = _nextSyntheticBlockId++;
            int failBlk = _nextSyntheticBlockId++;
            int mergeBlk = _nextSyntheticBlockId++;
            _currentOrderedBlockIds.Add(matchBlk);
            _currentOrderedBlockIds.Add(failBlk);
            _currentOrderedBlockIds.Add(mergeBlk);

            var matchStmts = new List<MirStatement>();
            if (resWrapperSym is not null)
            {
                int wId = GetOrCreateLocalId(resWrapperSym, isPatternBindingOverride: true);
                MirRvalue makeWrap = isOkArm
                    ? new MirRvalue.MakeOkWrapper(resType.Ok, new MirOperand.CopyOrMove(MirPlace.Local(bindingId), resType.Ok, span))
                    : new MirRvalue.MakeErrWrapper(resType.Err, new MirOperand.CopyOrMove(MirPlace.Local(bindingId), resType.Err, span));
                matchStmts.Add(new MirStatement.Assign(MirPlace.Local(wId), makeWrap, span));
            }
            matchStmts.Add(new MirStatement.Assign(
                MirPlace.Local(resLocalId),
                new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(!invert), MirType.Bool, span)),
                span));
            _mirBlockStmts[matchBlk] = matchStmts;
            _mirBlockTerms[matchBlk] = new MirTerminator.Goto(mergeBlk, span);

            _mirBlockStmts[failBlk] = new List<MirStatement>
            {
                new MirStatement.Assign(
                    MirPlace.Local(resLocalId),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(invert), MirType.Bool, span)),
                    span),
            };
            _mirBlockTerms[failBlk] = new MirTerminator.Goto(mergeBlk, span);

            int okBindingId = isOkArm ? bindingId : CreatePatternLocal(resType.Ok, span, "__ok_unused");
            int errBindingId = isOkArm ? CreatePatternLocal(resType.Err, span, "__err_unused") : bindingId;
            int okTarget = isOkArm ? matchBlk : failBlk;
            int errTarget = isOkArm ? failBlk : matchBlk;

            if (resExtraSub is not null)
            {
                bool consumedByVal = scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: false };
                if (isOkArm)
                {
                    okTarget = BuildExtraSubpatternCheckBlock(okBindingId, resType.Ok, consumedByVal, resExtraSub, okTarget, errTarget, span, _currentOrderedBlockIds);
                }
                else
                {
                    errTarget = BuildExtraSubpatternCheckBlock(errBindingId, resType.Err, consumedByVal, resExtraSub, errTarget, okTarget, span, _currentOrderedBlockIds);
                }
            }

            _mirBlockStmts[_currentBlockId] = _currentStmts;
            _mirBlockTerms[_currentBlockId] = new MirTerminator.MatchResult(scrutinee, okBindingId, okTarget, errBindingId, errTarget, span);
            _currentBlockId = mergeBlk;
            _currentStmts = new List<MirStatement>();
            return new MirOperand.CopyOrMove(MirPlace.Local(resLocalId), MirType.Bool, span);
        }

        if (scrutMirType is MirType.OptionType or MirType.OptionRefType or MirType.OptionMutType or MirType.ResultType &&
            ContainsUnionPattern(innerPattern))
        {
            var scrutinee = LowerMatchScrutinee(isPat.Value, scrutMirType, borrowMultiUseValueTemp: true);
            if (_module.IsLinearResource(scrutMirType))
            {
                EmitDiag(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Cannot borrow-match or logically combine patterns on linear union resource; consume it via '.IsNone'/'.IsErr', '.Unwrap()', or exhaustive 'Move(...)' match.",
                    span);
            }

            MirPlace scrutPlace;
            if (scrutinee is MirMatchScrutinee.PlaceScrutinee ps)
            {
                scrutPlace = ps.Place;
            }
            else
            {
                var rs = (MirMatchScrutinee.RvalueScrutinee)scrutinee;
                int scrutTmp = CreateTempLocal(scrutMirType, span, "__pat_scrut");
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(scrutTmp), new MirRvalue.Use(rs.Operand), span));
                scrutPlace = MirPlace.Local(scrutTmp);
            }

            int resLocalId = CreateTempLocal(MirType.Bool, span, "__is_pat");
            int matchBlk = _nextSyntheticBlockId++;
            int failBlk = _nextSyntheticBlockId++;
            int mergeBlk = _nextSyntheticBlockId++;
            _currentOrderedBlockIds.Add(matchBlk);
            _currentOrderedBlockIds.Add(failBlk);
            _currentOrderedBlockIds.Add(mergeBlk);

            _mirBlockStmts[matchBlk] = new List<MirStatement>
            {
                new MirStatement.Assign(
                    MirPlace.Local(resLocalId),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(!invert), MirType.Bool, span)),
                    span),
            };
            _mirBlockTerms[matchBlk] = new MirTerminator.Goto(mergeBlk, span);

            _mirBlockStmts[failBlk] = new List<MirStatement>
            {
                new MirStatement.Assign(
                    MirPlace.Local(resLocalId),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.BoolConst(invert), MirType.Bool, span)),
                    span),
            };
            _mirBlockTerms[failBlk] = new MirTerminator.Goto(mergeBlk, span);

            var term = BuildPatternCheckTerminator(
                scrutPlace,
                scrutMirType,
                scrutIsConsumedByValue: false,
                innerPattern,
                matchBlk,
                failBlk,
                span,
                _currentOrderedBlockIds);
            _mirBlockStmts[_currentBlockId] = _currentStmts;
            _mirBlockTerms[_currentBlockId] = term;
            _currentBlockId = mergeBlk;
            _currentStmts = new List<MirStatement>();
            return new MirOperand.CopyOrMove(MirPlace.Local(resLocalId), MirType.Bool, span);
        }

        var valOperand = LowerOperand(isPat.Value, scrutMirType);
        if (TryGetStrictConstantOperand(isPat.Value, scrutMirType, span, out var constantOperand))
        {
            valOperand = constantOperand;
        }
        return LowerNonUnionPatternTest(valOperand, scrutMirType, isPat.Pattern, span);
    }

    private MirOperand LowerNonUnionPatternTest(
        MirOperand scrutineeOp,
        MirType scrutineeType,
        IPatternOperation pattern,
        VibraniumSpan span)
    {
        switch (pattern)
        {
            case IDiscardPatternOperation:
                return new MirOperand.Const(new MirConstant.BoolConst(true), MirType.Bool, span);

            case ITypePatternOperation tp when SymbolEqualityComparer.Default.Equals(SubstituteType(tp.MatchedType), SubstituteType(tp.InputType)):
                return new MirOperand.Const(new MirConstant.BoolConst(true), MirType.Bool, span);

            case IDeclarationPatternOperation dp when dp.MatchedType is null || SymbolEqualityComparer.Default.Equals(SubstituteType(dp.MatchedType), SubstituteType(dp.InputType)):
                if (dp.DeclaredSymbol is ILocalSymbol loc)
                {
                    int lid = GetOrCreateLocalId(loc, isPatternBindingOverride: null);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(lid), new MirRvalue.Use(AsSwitchPatternSelection(pattern, scrutineeOp)), span));
                }
                return new MirOperand.Const(new MirConstant.BoolConst(true), MirType.Bool, span);

            case IConstantPatternOperation cp:
            {
                if (scrutineeType is MirType.StringLiteralType ||
                    cp.Value.Type?.SpecialType == SpecialType.System_String ||
                    cp.Value.ConstantValue is { HasValue: true, Value: string })
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.Unsupported,
                        "Constant 'string' pattern matching is banned in Vibranium; compare UTF-8 bytes via '.AsUtf8()' instead.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(cp.Syntax));
                    return new MirOperand.Const(new MirConstant.BoolConst(false), MirType.Bool, span);
                }
                if (scrutineeType is MirType.Float32Type or MirType.Float64Type && IsNaNConstantOperation(cp.Value))
                {
                    return EmitIsNaNTest(scrutineeOp, scrutineeType, span);
                }
                var rhs = LowerOperand(cp.Value, scrutineeType);
                if (TryFoldPatternComparison(scrutineeOp, rhs, scrutineeType, MirBinaryOp.Eq, span, out var folded))
                {
                    return folded;
                }
                int tmp = CreateTempLocal(MirType.Bool, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(tmp),
                    new MirRvalue.Binary(MirBinaryOp.Eq, scrutineeOp, rhs, MirType.Bool, false),
                    span));
                return new MirOperand.CopyOrMove(MirPlace.Local(tmp), MirType.Bool, span);
            }

            case IRelationalPatternOperation rp:
            {
                var rhs = LowerOperand(rp.Value, scrutineeType);
                var binOp = rp.OperatorKind switch
                {
                    BinaryOperatorKind.LessThan => MirBinaryOp.Lt,
                    BinaryOperatorKind.LessThanOrEqual => MirBinaryOp.Le,
                    BinaryOperatorKind.GreaterThan => MirBinaryOp.Gt,
                    BinaryOperatorKind.GreaterThanOrEqual => MirBinaryOp.Ge,
                    BinaryOperatorKind.NotEquals => MirBinaryOp.Ne,
                    _ => MirBinaryOp.Eq,
                };
                if (TryFoldPatternComparison(scrutineeOp, rhs, scrutineeType, binOp, span, out var folded))
                {
                    return folded;
                }
                int tmp = CreateTempLocal(MirType.Bool, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(tmp),
                    new MirRvalue.Binary(binOp, scrutineeOp, rhs, MirType.Bool, false),
                    span));
                return new MirOperand.CopyOrMove(MirPlace.Local(tmp), MirType.Bool, span);
            }

            case INegatedPatternOperation np:
            {
                var inner = LowerNonUnionPatternTest(scrutineeOp, scrutineeType, np.Pattern, span);
                if (inner is MirOperand.Const { Value: MirConstant.BoolConst value })
                {
                    return new MirOperand.Const(new MirConstant.BoolConst(!value.Value), MirType.Bool, span);
                }
                int tmp = CreateTempLocal(MirType.Bool, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(tmp),
                    new MirRvalue.Unary(MirUnaryOp.Not, inner, MirType.Bool, false),
                    span));
                return new MirOperand.CopyOrMove(MirPlace.Local(tmp), MirType.Bool, span);
            }

            case IBinaryPatternOperation bp:
            {
                var left = LowerNonUnionPatternTest(scrutineeOp, scrutineeType, bp.LeftPattern, span);
                var right = LowerNonUnionPatternTest(scrutineeOp, scrutineeType, bp.RightPattern, span);
                var op = bp.OperatorKind == BinaryOperatorKind.Or ? MirBinaryOp.BitOr : MirBinaryOp.BitAnd;
                // Lower both operands first: declaration-pattern assignments must survive folding.
                if (left is MirOperand.Const { Value: MirConstant.BoolConst l } &&
                    right is MirOperand.Const { Value: MirConstant.BoolConst r })
                {
                    bool value = op == MirBinaryOp.BitOr ? l.Value || r.Value : l.Value && r.Value;
                    return new MirOperand.Const(new MirConstant.BoolConst(value), MirType.Bool, span);
                }
                int tmp = CreateTempLocal(MirType.Bool, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(tmp),
                    new MirRvalue.Binary(op, left, right, MirType.Bool, false),
                    span));
                return new MirOperand.CopyOrMove(MirPlace.Local(tmp), MirType.Bool, span);
            }

            default:
                if (_module.IsLinearResource(scrutineeType))
                {
                    EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, $"Pattern '{pattern.Syntax}' cannot be used on linear resource '{scrutineeType}'.", span);
                }
                else
                {
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Pattern '{pattern.Syntax}' is not supported in Vibranium.", span);
                }
                return new MirOperand.Const(new MirConstant.BoolConst(false), MirType.Bool, span);
        }
    }

    private MirOperand EmitIsNaNTest(MirOperand scrutineeOp, MirType scrutineeType, VibraniumSpan span)
    {
        if (scrutineeOp is MirOperand.Const { Value: MirConstant.Float32Const f })
        {
            return new MirOperand.Const(new MirConstant.BoolConst(float.IsNaN(f.Value)), MirType.Bool, span);
        }
        if (scrutineeOp is MirOperand.Const { Value: MirConstant.Float64Const d })
        {
            return new MirOperand.Const(new MirConstant.BoolConst(double.IsNaN(d.Value)), MirType.Bool, span);
        }
        MirOperand tOp = scrutineeOp;
        if (tOp is not MirOperand.CopyOrMove { Place: { LocalId: var lid, Projections.Count: 0 } } ||
            (lid >= 0 && lid < _locals.Count && _locals[lid].IsRefLocal))
        {
            int valTmp = CreateTempLocal(scrutineeType, span, "__nan_scrut");
            _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(valTmp), new MirRvalue.Use(scrutineeOp), span));
            tOp = new MirOperand.CopyOrMove(MirPlace.Local(valTmp), scrutineeType, span);
        }

        int eqTmp = CreateTempLocal(MirType.Bool, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(eqTmp),
            new MirRvalue.Binary(MirBinaryOp.Eq, tOp, tOp, MirType.Bool, false),
            span));

        int isNanTmp = CreateTempLocal(MirType.Bool, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(isNanTmp),
            new MirRvalue.Unary(
                MirUnaryOp.Not,
                new MirOperand.CopyOrMove(MirPlace.Local(eqTmp), MirType.Bool, span),
                MirType.Bool,
                false),
            span));

        return new MirOperand.CopyOrMove(MirPlace.Local(isNanTmp), MirType.Bool, span);
    }

    private (bool Names, bool Repoints) RefRepointDestinationUses(ISimpleAssignmentOperation assignment, ILocalSymbol destination)
    {
        if (assignment.Syntax is not AssignmentExpressionSyntax { Right: RefExpressionSyntax rhs })
        {
            return (false, false);
        }

        // Inspect the entire source RHS, not lowered or reachable CFG operands: every
        // occurrence of this symbol counts, including a nested ref assignment target.
        var model = _compilation.GetSemanticModel(rhs.SyntaxTree);
        bool names = false;
        foreach (var name in rhs.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (name.Identifier.ValueText != destination.Name ||
                !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, destination))
            {
                continue;
            }
            names = true;
            SyntaxNode target = name;
            while (target.Parent is ParenthesizedExpressionSyntax grouping) target = grouping;
            if (target.Parent is AssignmentExpressionSyntax { Right: RefExpressionSyntax } nested && nested.Left == target)
            {
                return (true, true);
            }
        }
        return (names, false);
    }

    internal const string ConditionalRefReassignmentMessage =
        "Conditional and short-circuit expressions in a ref reassignment are not supported in Vibranium v0.2; compute the value in a local first, or use an if statement.";

    private static bool TryGetConditionalRefReassignmentKeyword(AssignmentExpressionSyntax assignment, out SyntaxToken keyword)
    {
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right is RefExpressionSyntax rhs &&
            rhs.Expression.DescendantNodesAndSelf().Any(node => node.Kind() is
                SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression or SyntaxKind.ConditionalExpression or
                SyntaxKind.SwitchExpression or SyntaxKind.IsPatternExpression or SyntaxKind.IsExpression))
        {
            keyword = rhs.RefKeyword;
            return true;
        }
        keyword = default;
        return false;
    }

    // C25 rejects this ref reassignment and its value is not lowered, so a capture inside it binds nothing.
    private static bool IsInConditionalRefReassignment(IOperation op) =>
        op.Syntax.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().Any(a => TryGetConditionalRefReassignmentKeyword(a, out _));

    internal static void CheckConditionalRefReassignments(SyntaxNode root, List<VibraniumDiagnostic> diagnostics)
    {
        // CFG ordering can visit a nested assignment first, or fold away the offending expression.
        // Report the first RHS ref keyword in source order, once per file.
        SyntaxToken first = default;
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (TryGetConditionalRefReassignmentKeyword(assignment, out var keyword) &&
                (first.RawKind == 0 || keyword.SpanStart < first.SpanStart))
            {
                first = keyword;
            }
        }
        if (first.RawKind != 0)
        {
            diagnostics.Add(new VibraniumDiagnostic(VibraniumDiagnosticIds.Unsupported, ConditionalRefReassignmentMessage,
                VibraniumSymbolHelpers.ToVibraniumSpan(first.GetLocation())));
        }
    }

    private MirOperand? LowerSimpleAssignment(ISimpleAssignmentOperation assign, bool wantResult)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(assign.Syntax);

        if (assign.IsRef)
        {
            if (assign.Syntax is AssignmentExpressionSyntax source && TryGetConditionalRefReassignmentKeyword(source, out _))
            {
                // The source-order pass already reported C25. Do not attempt to lower or bind the rejected RHS.
                if (!wantResult) return null;
                var type = MapMirType(SubstituteType(assign.Type));
                return new MirOperand.Const(new MirConstant.ZeroDefault(type), type, span);
            }
            if (assign.Target is not ILocalReferenceOperation refTarget)
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Unsupported ref assignment target.", span);
                return null;
            }

            int refLocalId = GetOrCreateLocalId(refTarget.Local, isPatternBindingOverride: null);
            bool isMut = refTarget.Local.RefKind != RefKind.RefReadOnly;
            var derefValType = MapMirType(SubstituteType(refTarget.Local.Type));

            var valOp = UnwrapImplicitIdentityConversion(assign.Value);
            var (namesOldRef, repointsSameRef) = RefRepointDestinationUses(assign, refTarget.Local);
            int? oldLoanSnapshot = null;
            if (repointsSameRef)
            {
                // Pin the original loan, not the intermediate value a nested re-point may store.
                int snapshot = CreateTempRefLocal(derefValType, isMut, span, "__old_ref_loan");
                _locals[snapshot] = _locals[snapshot] with { RefRepointSnapshotOf = refLocalId };
                _currentStmts.Add(new MirStatement.RememberRefRepointLoan(snapshot, refLocalId, span));
                oldLoanSnapshot = snapshot;
            }
            bool keepDestinationLive = namesOldRef && !repointsSameRef;
            if (TryEmitRefProducingOperationInto(valOp, refLocalId, isMut, span))
            {
                if (keepDestinationLive)
                {
                    int producer = _currentStmts.Count - 1;
                    _currentStmts[producer] = _currentStmts[producer] with { RefRepointNamesDestination = true };
                }
                if (oldLoanSnapshot is int snapshot)
                {
                    _currentStmts.Add(new MirStatement.KeepAnalysisBorrow(snapshot, span));
                }
                return wantResult
                    ? new MirOperand.CopyOrMove(new MirPlace(refLocalId, new MirProjection[] { new MirProjection.Deref(derefValType) }), derefValType, span)
                    : null;
            }

            if (isMut && valOp is IParameterReferenceOperation refPr && IsCapturedPrimaryConstructorParameter(refPr.Parameter))
            {
                EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot take mutable reference to captured primary-constructor parameter '{refPr.Parameter.Name}'.", span);
            }
            var targetPlace = LowerPlace(valOp, isMutableAccess: isMut);
            _currentStmts.Add(new MirStatement.BindRef(refLocalId, isMut, targetPlace, span)
            {
                RefRepointNamesDestination = keepDestinationLive,
            });
            if (oldLoanSnapshot is int retainedSnapshot)
            {
                _currentStmts.Add(new MirStatement.KeepAnalysisBorrow(retainedSnapshot, span));
            }
            return wantResult
                ? new MirOperand.CopyOrMove(new MirPlace(refLocalId, new MirProjection[] { new MirProjection.Deref(derefValType) }), derefValType, span)
                : null;
        }

        var targetMirType = MapMirType(SubstituteType(assign.Target.Type ?? assign.Value.Type));

        if (assign.Target is IDiscardOperation)
        {
            int discardId = CreateTempLocal(targetMirType, span, "__discard_assign");
            var discardPlace = MirPlace.Local(discardId);
            EmitAssignFromExpression(discardPlace, assign.Value, targetMirType, span);
            if (_module.IsLinearResource(targetMirType))
            {
                EmitDiag(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Cannot discard linear IDisposable resource with '_ = ...'; bind it with 'using var' or consume it.",
                    span);
            }
            return wantResult ? new MirOperand.CopyOrMove(discardPlace, targetMirType, span) : null;
        }

        if (UnwrapCapturedSingleOperation(assign.Target) is IParameterReferenceOperation assignPr &&
            IsCapturedPrimaryConstructorParameter(assignPr.Parameter))
        {
            EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot reassign captured primary-constructor parameter '{assignPr.Parameter.Name}'; declare an explicit mutable field instead.", span);
        }

        var unwrappedTarget = UnwrapImplicitIdentityConversion(assign.Target);
        if (unwrappedTarget is IFlowCaptureReferenceOperation saSetFcr &&
            _captures.TryGetValue(saSetFcr.Id, out var saSetCap) &&
            saSetCap.Kind == CaptureKind.SetterPropertyOrIndexer &&
            saSetCap.SubstSetter is { } capSetter)
        {
            var rhsOp = LowerOperand(assign.Value, targetMirType);
            if (saSetCap.SetRecvPass == MirPassKind.ImplicitThisMut && rhsOp is MirOperand.CopyOrMove && targetMirType is not MirType.SpanType)
            {
                int rhsTmp = CreateTempLocal(targetMirType, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(rhsTmp), new MirRvalue.Use(rhsOp), span));
                rhsOp = new MirOperand.CopyOrMove(MirPlace.Local(rhsTmp), targetMirType, span);
            }
            var callArgs = new List<MirCallArg>();
            if (saSetCap.SetRecvPass is { } setPass && saSetCap.CapturedRecvPlace is { } setRecv)
            {
                callArgs.Add(WithCallHold(new MirCallArg(setPass, null, setRecv, saSetCap.CapturedRecvSpan), saSetCap.Hold));
            }
            if (saSetCap.CapturedIndexArgs is { Count: > 0 } idxArgs)
            {
                callArgs.AddRange(idxArgs);
            }
            callArgs.Add(new MirCallArg(MirPassKind.ByValue, rhsOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(assign.Value.Syntax)));
            _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(capSetter), callArgs, span));
            return wantResult ? rhsOp : null;
        }

        if (unwrappedTarget is IPropertyReferenceOperation { Property.SetMethod: { } propSetter } setProp &&
            propSetter.ContainingType is INamedTypeSymbol setOwner &&
            IsInCompilationOrVibraniumStd(setOwner.ContainingAssembly))
        {
            var substSetter = SubstituteMethod(propSetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(setOwner);

            var callArgs = new List<MirCallArg>();
            MirPassKind? setRecvPass = null;
            if (!substSetter.IsStatic && setProp.Instance is not null)
            {
                setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
                bool isMutSetRecv = setRecvPass == MirPassKind.ImplicitThisMut;
                var recvMirT = MapMirType(SubstituteType(setProp.Instance.Type));
                var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(setProp.Instance.Syntax);
                var wrapperRead = ReadsReceiverInPlace(substSetter, setProp.Instance, substSetter.Parameters.Length) ? BeginWrapperRead(setProp.Instance) : null;
                var recvPlace = isMutSetRecv
                    ? LowerPlace(setProp.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(setProp.Instance, recvMirT, isMutableAccess: false, recvSpan);
                EndWrapperRead(wrapperRead);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: isMutSetRecv, isTwoPhaseBorrow: isMutSetRecv, recvSpan);
                callArgs.Add(WithCallHold(new MirCallArg(setRecvPass.Value, null, recvPlace, recvSpan), wrapperRead));
            }
            if (setProp.Arguments.Length > 0)
            {
                var idxParams = substSetter.Parameters.Take(substSetter.Parameters.Length - 1).ToImmutableArray();
                callArgs.AddRange(OrderCallArguments(setProp.Arguments, idxParams, span, materializeByValueTemps: setRecvPass == MirPassKind.ImplicitThisMut));
            }
            var rhsOp = LowerOperand(assign.Value, targetMirType);
            if (setRecvPass == MirPassKind.ImplicitThisMut && rhsOp is MirOperand.CopyOrMove && targetMirType is not MirType.SpanType)
            {
                int rhsTmp = CreateTempLocal(targetMirType, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(rhsTmp), new MirRvalue.Use(rhsOp), span));
                rhsOp = new MirOperand.CopyOrMove(MirPlace.Local(rhsTmp), targetMirType, span);
            }
            callArgs.Add(new MirCallArg(MirPassKind.ByValue, rhsOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(assign.Value.Syntax)));
            _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(substSetter), callArgs, span));
            return wantResult ? rhsOp : null;
        }

        if (unwrappedTarget is IImplicitIndexerReferenceOperation
            {
                IndexerSymbol: IPropertySymbol { SetMethod: { } implSetter },
                LengthSymbol: { } implSetLenSym,
            } setImpl &&
            SubstituteType(setImpl.Argument.Type)?.ToDisplayString() == "System.Index" &&
            implSetter.ContainingType is INamedTypeSymbol implSetOwner &&
            IsInCompilationOrVibraniumStd(implSetOwner.ContainingAssembly))
        {
            var substImplSetter = SubstituteMethod(implSetter);
            _enqueueMethodInstantiation(substImplSetter);
            _enqueueTypeInstantiation(implSetOwner);

            var setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substImplSetter);
            bool isMutSetRecv = setRecvPass == MirPassKind.ImplicitThisMut;
            bool needMutSetRecv = isMutSetRecv || (IsFromEndIndexOperation(setImpl.Argument) && IsLengthSymbolMutating(implSetLenSym));
            var recvMirT = MapMirType(SubstituteType(setImpl.Instance.Type));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(setImpl.Instance.Syntax);
            var recvPlace = needMutSetRecv
                ? LowerPlace(setImpl.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(setImpl.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutSetRecv, isTwoPhaseBorrow: needMutSetRecv, recvSpan);
            var idxOp = SnapshotCopyOperandIfNeeded(LowerSystemIndexGeneric(setImpl.Argument, recvPlace, implSetLenSym, span), MirType.Int32, span);
            var rhsOp = LowerOperand(assign.Value, targetMirType);
            if (needMutSetRecv && rhsOp is MirOperand.CopyOrMove && targetMirType is not MirType.SpanType)
            {
                int rhsTmp = CreateTempLocal(targetMirType, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(rhsTmp), new MirRvalue.Use(rhsOp), span));
                rhsOp = new MirOperand.CopyOrMove(MirPlace.Local(rhsTmp), targetMirType, span);
            }
            var callArgs = new List<MirCallArg>(3)
            {
                new(setRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
                new(MirPassKind.ByValue, rhsOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(assign.Value.Syntax)),
            };
            _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(substImplSetter), callArgs, span));
            return wantResult ? rhsOp : null;
        }

        if (!IsVecOrSpanIndexerAccess(assign.Target))
        {
            // Direct local variable assignment (when local is NOT a ref local — writing to a ref local with IsRef==false is an indirect write through Deref!)
            if (assign.Target is ILocalReferenceOperation directLoc)
            {
                int destId = GetOrCreateLocalId(directLoc.Local, isPatternBindingOverride: null);
                if (!_locals[destId].IsRefLocal)
                {
                    var destPlace = MirPlace.Local(destId);
                    EmitAssignFromExpression(destPlace, assign.Value, targetMirType, span);
                    return wantResult
                        ? SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(destPlace, targetMirType, span), targetMirType, span)
                        : null;
                }
            }

            // Evaluate LHS lvalue strictly before RHS in C# left-to-right order
            var lhsPlace = LowerPlace(assign.Target, isMutableAccess: true, isTwoPhaseBorrow: true);
            lhsPlace = SnapshotAssignmentTarget(lhsPlace, targetMirType, span);
            var rhsRvalue = LowerRvalue(assign.Value, targetMirType);
            if (wantResult && _module.IsCopy(targetMirType))
            {
                if (rhsRvalue is MirRvalue.StackSpan && lhsPlace.Projections.Count == 0 &&
                    lhsPlace.LocalId >= 0 && lhsPlace.LocalId < _locals.Count &&
                    _locals[lhsPlace.LocalId] is { IsParam: true, PassKind: MirPassKind.ByValue })
                {
                    // A collection expression's storage is owned by its destination's scope also when the assignment's
                    // value is used (`return p = [1, 2];`, SPEC 3.7). Store into the by-value parameter and snapshot
                    // it, as for a local destination, so that no compiler temp owns the storage.
                    _currentStmts.Add(new MirStatement.Assign(lhsPlace, rhsRvalue, span));
                    return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(lhsPlace, targetMirType, span), targetMirType, span);
                }
                int resTemp = CreateTempLocal(targetMirType, span, "__assign_res");
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(resTemp), rhsRvalue, span));
                var resOp = new MirOperand.CopyOrMove(MirPlace.Local(resTemp), targetMirType, span);
                _currentStmts.Add(new MirStatement.Assign(lhsPlace, new MirRvalue.Use(resOp), span));
                return resOp;
            }
            _currentStmts.Add(new MirStatement.Assign(lhsPlace, rhsRvalue, span));
            return wantResult ? new MirOperand.CopyOrMove(lhsPlace, targetMirType, span) : null;
        }
        else
        {
            // In C#, Vec<T>[idx] / Span<T>[idx] evaluates receiver -> idx -> ref-indexer bounds check BEFORE evaluating RHS!
            var elemPlace = LowerPlace(assign.Target, isMutableAccess: true, isTwoPhaseBorrow: true);
            var rhsOp = LowerOperand(assign.Value, targetMirType);
            _currentStmts.Add(new MirStatement.Assign(elemPlace, new MirRvalue.Use(rhsOp), span));
            return wantResult ? rhsOp : null;
        }
    }

    private void EmitAssignFromExpression(MirPlace destPlace, IOperation valueOp, MirType targetMirType, VibraniumSpan span)
    {
        var unwrapped = UnwrapImplicitIdentityConversion(valueOp);
        if (unwrapped is IInvocationOperation inv)
        {
            var resOp = LowerInvocation(inv, destPlace);
            if (resOp is not null && !PlacedIntoDest(resOp, destPlace))
            {
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(resOp), span));
            }
            else if (resOp is null && !_module.IsLinearResource(targetMirType))
            {
                _currentStmts.Add(new MirStatement.Assign(
                    destPlace,
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(targetMirType), targetMirType, span)),
                    span));
            }
            return;
        }

        if (TryEmitSpecialIntrinsicExpressionInto(valueOp, destPlace, targetMirType, span))
        {
            return;
        }

        var rv = LowerRvalue(valueOp, targetMirType);
        _currentStmts.Add(new MirStatement.Assign(destPlace, rv, span));
    }

    private static bool PlacedIntoDest(MirOperand op, MirPlace destPlace) =>
        op is MirOperand.CopyOrMove cm &&
        cm.Place.LocalId == destPlace.LocalId &&
        cm.Place.Projections.Count == destPlace.Projections.Count;

    private MirOperand? LowerCompoundAssignment(ICompoundAssignmentOperation compound, bool wantResult)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(compound.Syntax);
        var targetTypeSym = SubstituteType(compound.Target.Type);

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(targetTypeSym, "Vec") &&
            compound.OperatorKind == BinaryOperatorKind.Add &&
            targetTypeSym is INamedTypeSymbol vecNamed &&
            vecNamed.TypeArguments.Length == 1)
        {
            var elemType = MapMirType(SubstituteType(vecNamed.TypeArguments[0]));
            if (UnwrapImplicitIdentityConversion(compound.Target) is IParameterReferenceOperation vecPr &&
                IsCapturedPrimaryConstructorParameter(vecPr.Parameter))
            {
                // Mutable operation on captured primary ctor parameter borrows `this.<param>P` via ImplicitThisMut / VIB004
            }
            var vecPlace = LowerPlace(compound.Target, isMutableAccess: true, isTwoPhaseBorrow: true);
            vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, new MirType.VecType(elemType), isMutable: true, isTwoPhaseBorrow: true, span);
            var itemOp = LowerOperand(compound.Value, elemType);
            if (itemOp is MirOperand.CopyOrMove)
            {
                int itemTemp = CreateTempLocal(elemType, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(itemTemp), new MirRvalue.Use(itemOp), span));
                itemOp = new MirOperand.CopyOrMove(MirPlace.Local(itemTemp), elemType, span);
            }
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.VecPush,
                null,
                null,
                false,
                new[] { itemOp },
                new[] { vecPlace },
                MirType.Void,
                span));
            return null;
        }

        var valMirType = MapMirType(targetTypeSym);

        if (compound.OperatorMethod is { } opMethod)
        {
            if (VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(opMethod) &&
                IsNonRefPropertyOrIndexer(compound.Target))
            {
                EmitDiag(
                    VibraniumDiagnosticIds.Unsupported,
                    "Compound assignment with user-defined 'operator +=' on a non-ref property or indexer is not supported in Vibranium.",
                    span);
                return wantResult
                    ? new MirOperand.Const(new MirConstant.ZeroDefault(valMirType), valMirType, span)
                    : null;
            }

            if (VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(opMethod) &&
                opMethod.ContainingType is INamedTypeSymbol opOwner &&
                IsInCompilationOrVibraniumStd(opOwner.ContainingAssembly))
            {
                var substOp = SubstituteMethod(opMethod);
                _enqueueMethodInstantiation(substOp);
                _enqueueTypeInstantiation(opOwner);

                var recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substOp);
                bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
                var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(compound.Target.Syntax);
                var recvPlace = isMutRecv
                    ? LowerPlace(compound.Target, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(compound.Target, valMirType, isMutableAccess: false, recvSpan);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, valMirType, isMutable: isMutRecv, isTwoPhaseBorrow: isMutRecv, recvSpan);

                var paramMirT = MapMirType(SubstituteType(substOp.Parameters[0].Type));
                var rhsSpan = VibraniumSymbolHelpers.ToVibraniumSpan(compound.Value.Syntax);
                MirOperand argOp;
                if (substOp.Parameters[0].Type.SpecialType == SpecialType.System_Byte && false)
                {
                    argOp = LowerOperand(compound.Value, paramMirT);
                }
                else
                {
                    argOp = LowerOperand(compound.Value, paramMirT);
                }
                if (isMutRecv && argOp is MirOperand.CopyOrMove && paramMirT is not MirType.SpanType)
                {
                    int argTmp = CreateTempLocal(paramMirT, rhsSpan);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(argTmp), new MirRvalue.Use(argOp), rhsSpan));
                    argOp = new MirOperand.CopyOrMove(MirPlace.Local(argTmp), paramMirT, rhsSpan);
                }

                var opCallArgs = new List<MirCallArg>(2)
                {
                    new(recvPass, null, recvPlace, recvSpan),
                    new(MirPassKind.ByValue, argOp, null, rhsSpan),
                };
                _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(substOp), opCallArgs, span));
                return null;
            }

            return wantResult
                ? new MirOperand.Const(new MirConstant.ZeroDefault(valMirType), valMirType, span)
                : null;
        }

        var unwrappedTarget = UnwrapImplicitIdentityConversion(compound.Target);
        if (UnwrapCapturedSingleOperation(compound.Target) is IParameterReferenceOperation compPr &&
            IsCapturedPrimaryConstructorParameter(compPr.Parameter))
        {
            EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot reassign captured primary-constructor parameter '{compPr.Parameter.Name}'; declare an explicit mutable field instead.", span);
        }

        var rhsMirType = MapMirType(SubstituteType(compound.Value.Type));

        if (unwrappedTarget is IFlowCaptureReferenceOperation caSetFcr &&
            _captures.TryGetValue(caSetFcr.Id, out var caSetCap) &&
            caSetCap.Kind == CaptureKind.SetterPropertyOrIndexer &&
            caSetCap.SubstSetter is { } capCompSetter)
        {
            var oldOp = new MirOperand.CopyOrMove(MirPlace.Local(caSetCap.PrimaryLocalId), valMirType, span);
            var propRhsOp = LowerOperand(compound.Value, rhsMirType);
            var propResOp = EmitCompoundBinaryResult(
                compound.OperatorKind,
                oldOp,
                valMirType,
                propRhsOp,
                rhsMirType,
                compound.IsChecked,
                span);

            var setArgs = new List<MirCallArg>();
            if (caSetCap.SetRecvPass is { } setPass && caSetCap.CapturedRecvPlace is { } setRecv)
            {
                setArgs.Add(new MirCallArg(setPass, null, setRecv, caSetCap.CapturedRecvSpan));
            }
            if (caSetCap.CapturedIndexArgs is { Count: > 0 } idxArgs)
            {
                setArgs.AddRange(idxArgs);
            }
            setArgs.Add(new MirCallArg(MirPassKind.ByValue, propResOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(compound.Value.Syntax)));
            _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(capCompSetter), setArgs, span));
            return wantResult ? propResOp : null;
        }

        // Property / explicit indexer compound assignment (`c.P += rhs`, `d[i] += rhs`)
        if (unwrappedTarget is IPropertyReferenceOperation
            {
                Property:
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    GetMethod: { } propGetter,
                    SetMethod: { } propSetter,
                },
            } propRef &&
            propSetter.ContainingType is INamedTypeSymbol propOwner &&
            IsInCompilationOrVibraniumStd(propOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(propGetter);
            var substSetter = SubstituteMethod(propSetter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(propOwner);

            MirPassKind? getRecvPass = null;
            MirPassKind? setRecvPass = null;
            MirPlace? recvPlace = null;
            VibraniumSpan recvSpan = span;

            if (!substSetter.IsStatic && propRef.Instance is not null)
            {
                getRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
                bool needMutRecv = getRecvPass == MirPassKind.ImplicitThisMut || setRecvPass == MirPassKind.ImplicitThisMut;
                var recvMirT = MapMirType(SubstituteType(propRef.Instance.Type));
                recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(propRef.Instance.Syntax);
                recvPlace = needMutRecv
                    ? LowerPlace(propRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(propRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);
            }

            var evalIdxArgs = propRef.Arguments.Length > 0
                ? OrderCallArguments(propRef.Arguments, substGetter.Parameters, span, materializeByValueTemps: true)
                : new List<MirCallArg>();

            var getArgs = new List<MirCallArg>(evalIdxArgs.Count + 1);
            if (getRecvPass is not null && recvPlace is not null)
            {
                getArgs.Add(new MirCallArg(getRecvPass.Value, null, recvPlace, recvSpan));
            }
            AddGetterIndexArgs(getArgs, evalIdxArgs);

            int propOldTemp = CreateTempLocal(valMirType, span, "__cmp_old");
            _currentStmts.Add(new MirStatement.Call(
                MirPlace.Local(propOldTemp),
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substGetter),
                getArgs,
                span));
            var propOldOp = new MirOperand.CopyOrMove(MirPlace.Local(propOldTemp), valMirType, span);

            var propRhsOp = LowerOperand(compound.Value, rhsMirType);
            var propResOp = EmitCompoundBinaryResult(
                compound.OperatorKind,
                propOldOp,
                valMirType,
                propRhsOp,
                rhsMirType,
                compound.IsChecked,
                span);

            var setArgs = new List<MirCallArg>(evalIdxArgs.Count + 2);
            if (setRecvPass is not null && recvPlace is not null)
            {
                setArgs.Add(new MirCallArg(setRecvPass.Value, null, recvPlace, recvSpan));
            }
            setArgs.AddRange(evalIdxArgs);
            setArgs.Add(new MirCallArg(MirPassKind.ByValue, propResOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(compound.Value.Syntax)));

            _currentStmts.Add(new MirStatement.Call(
                null,
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substSetter),
                setArgs,
                span));
            return wantResult ? propResOp : null;
        }

        // Implicit `^Index` user indexer compound assignment (`d[^i] += rhs`)
        if (unwrappedTarget is IImplicitIndexerReferenceOperation
            {
                IndexerSymbol: IPropertySymbol
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    GetMethod: { } implGetter,
                    SetMethod: { } implSetter,
                },
                LengthSymbol: { } implLenSym,
            } implRef &&
            SubstituteType(implRef.Argument.Type)?.ToDisplayString() == "System.Index" &&
            implSetter.ContainingType is INamedTypeSymbol implOwner &&
            IsInCompilationOrVibraniumStd(implOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(implGetter);
            var substSetter = SubstituteMethod(implSetter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(implOwner);

            var getRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
            var setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
            bool needMutRecv =
                getRecvPass == MirPassKind.ImplicitThisMut ||
                setRecvPass == MirPassKind.ImplicitThisMut ||
                (IsFromEndIndexOperation(implRef.Argument) && IsLengthSymbolMutating(implLenSym));

            var recvMirT = MapMirType(SubstituteType(implRef.Instance.Type));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implRef.Instance.Syntax);
            var recvPlace = needMutRecv
                ? LowerPlace(implRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(implRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

            var idxOp = SnapshotCopyOperandIfNeeded(
                LowerSystemIndexGeneric(implRef.Argument, recvPlace, implLenSym, span),
                MirType.Int32,
                span);

            int implOldTemp = CreateTempLocal(valMirType, span, "__cmp_old");
            var getArgs = new List<MirCallArg>(2)
            {
                new(getRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
            };
            _currentStmts.Add(new MirStatement.Call(
                MirPlace.Local(implOldTemp),
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substGetter),
                getArgs,
                span));
            var implOldOp = new MirOperand.CopyOrMove(MirPlace.Local(implOldTemp), valMirType, span);

            var implRhsOp = LowerOperand(compound.Value, rhsMirType);
            var implResOp = EmitCompoundBinaryResult(
                compound.OperatorKind,
                implOldOp,
                valMirType,
                implRhsOp,
                rhsMirType,
                compound.IsChecked,
                span);

            var setArgs = new List<MirCallArg>(3)
            {
                new(setRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
                new(MirPassKind.ByValue, implResOp, null, VibraniumSymbolHelpers.ToVibraniumSpan(compound.Value.Syntax)),
            };
            _currentStmts.Add(new MirStatement.Call(
                null,
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substSetter),
                setArgs,
                span));
            return wantResult ? implResOp : null;
        }

        // 1. Evaluate LHS lvalue once (including receiver, index, and bounds check) BEFORE RHS
        var targetPlace = LowerPlace(compound.Target, isMutableAccess: true, isTwoPhaseBorrow: true);
        targetPlace = SnapshotAssignmentTarget(targetPlace, valMirType, span);

        // 2. Snapshot old LHS value into a fresh temp BEFORE lowering RHS
        int oldTemp = GetOrSnapshotUpdateOldValue(compound.Target, targetPlace, valMirType, span);
        var oldOperand = new MirOperand.CopyOrMove(MirPlace.Local(oldTemp), valMirType, span);

        // 3. Lower RHS
        var rhsOp = LowerOperand(compound.Value, rhsMirType);

        // 4. Compute binary op and write back
        var resOperand = EmitCompoundBinaryResult(
            compound.OperatorKind,
            oldOperand,
            valMirType,
            rhsOp,
            rhsMirType,
            compound.IsChecked,
            span);
        _currentStmts.Add(new MirStatement.Assign(targetPlace, new MirRvalue.Use(resOperand), span));
        return wantResult ? resOperand : null;
    }

    private int GetOrSnapshotUpdateOldValue(IOperation target, MirPlace place, MirType type, VibraniumSpan span)
    {
        if (UnwrapImplicitIdentityConversion(target) is IFlowCaptureReferenceOperation captured &&
            _captures.TryGetValue(captured.Id, out var info) && info.UpdateOldValueLocalId >= 0)
        {
            return info.UpdateOldValueLocalId;
        }
        int oldTemp = CreateTempLocal(type, span, "__cmp_old");
        _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(oldTemp),
            new MirRvalue.Use(new MirOperand.CopyOrMove(place, type, span)), span));
        return oldTemp;
    }

    private MirOperand EmitCompoundBinaryResult(
        BinaryOperatorKind operatorKind,
        MirOperand oldOperand,
        MirType valMirType,
        MirOperand rhsOp,
        MirType rhsMirType,
        bool isChecked,
        VibraniumSpan span)
    {
        var binOp = MapBinaryOp(operatorKind);
        int resTemp = CreateTempLocal(valMirType, span);

        if (MirType.IsInteger(valMirType) && MirType.GetIntegerBitWidth(valMirType) < 32)
        {
            int oldI32 = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(oldI32),
                new MirRvalue.Cast(oldOperand, MirType.Int32, false),
                span));
            var lhsI32Op = new MirOperand.CopyOrMove(MirPlace.Local(oldI32), MirType.Int32, span);

            MirOperand rhsI32Op = rhsOp;
            if (!Equals(rhsMirType, MirType.Int32))
            {
                int r32 = CreateTempLocal(MirType.Int32, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(r32),
                    new MirRvalue.Cast(rhsOp, MirType.Int32, false),
                    span));
                rhsI32Op = new MirOperand.CopyOrMove(MirPlace.Local(r32), MirType.Int32, span);
            }

            int sumI32 = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(sumI32),
                new MirRvalue.Binary(binOp, lhsI32Op, rhsI32Op, MirType.Int32, isChecked),
                span));

            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(resTemp),
                new MirRvalue.Cast(new MirOperand.CopyOrMove(MirPlace.Local(sumI32), MirType.Int32, span), valMirType, isChecked),
                span));
        }
        else
        {
            MirOperand adjustedRhs = rhsOp;
            if (binOp is not (MirBinaryOp.Shl or MirBinaryOp.Shr or MirBinaryOp.UShr) && !Equals(rhsMirType, valMirType))
            {
                int rCast = CreateTempLocal(valMirType, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(rCast),
                    new MirRvalue.Cast(rhsOp, valMirType, false),
                    span));
                adjustedRhs = new MirOperand.CopyOrMove(MirPlace.Local(rCast), valMirType, span);
            }

            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(resTemp),
                new MirRvalue.Binary(binOp, oldOperand, adjustedRhs, valMirType, isChecked),
                span));
        }

        return new MirOperand.CopyOrMove(MirPlace.Local(resTemp), valMirType, span);
    }

    private MirOperand LowerIncrementOrDecrement(IIncrementOrDecrementOperation incDec, bool wantResult)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(incDec.Syntax);
        var valType = MapMirType(SubstituteType(incDec.Target.Type));

        if (incDec.OperatorMethod is not null)
        {
            return new MirOperand.Const(new MirConstant.ZeroDefault(valType), valType, span);
        }

        var unwrappedTarget = UnwrapImplicitIdentityConversion(incDec.Target);
        if (UnwrapCapturedSingleOperation(incDec.Target) is IParameterReferenceOperation incPr &&
            IsCapturedPrimaryConstructorParameter(incPr.Parameter))
        {
            EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot mutate captured primary-constructor parameter '{incPr.Parameter.Name}'; declare an explicit mutable field instead.", span);
        }

        MirOperand MakeStepConst() => valType switch
        {
            MirType.Int64Type or MirType.UInt64Type or MirType.NIntType or MirType.NUIntType =>
                new MirOperand.Const(new MirConstant.Int64Const(1L), valType, span),
            MirType.Float32Type => new MirOperand.Const(new MirConstant.Float32Const(1f), valType, span),
            MirType.Float64Type => new MirOperand.Const(new MirConstant.Float64Const(1.0), valType, span),
            _ => new MirOperand.Const(new MirConstant.Int32Const(1), valType, span),
        };

        var binOp = incDec.Kind == OperationKind.Increment ? MirBinaryOp.Add : MirBinaryOp.Sub;

        if (unwrappedTarget is IFlowCaptureReferenceOperation idSetFcr &&
            _captures.TryGetValue(idSetFcr.Id, out var idSetCap) &&
            idSetCap.Kind == CaptureKind.SetterPropertyOrIndexer &&
            idSetCap.SubstSetter is { } capIncSetter)
        {
            int propNewTemp = CreateTempLocal(valType, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(propNewTemp),
                new MirRvalue.Binary(binOp, new MirOperand.CopyOrMove(MirPlace.Local(idSetCap.PrimaryLocalId), valType, span), MakeStepConst(), valType, incDec.IsChecked),
                span));
            var newValOp = new MirOperand.CopyOrMove(MirPlace.Local(propNewTemp), valType, span);

            var setArgs = new List<MirCallArg>();
            if (idSetCap.SetRecvPass is { } setPass && idSetCap.CapturedRecvPlace is { } setRecv)
            {
                setArgs.Add(new MirCallArg(setPass, null, setRecv, idSetCap.CapturedRecvSpan));
            }
            if (idSetCap.CapturedIndexArgs is { Count: > 0 } idxArgs)
            {
                setArgs.AddRange(idxArgs);
            }
            setArgs.Add(new MirCallArg(MirPassKind.ByValue, newValOp, null, span));
            _currentStmts.Add(new MirStatement.Call(null, null, false, VibraniumSymbolHelpers.GetMethodKey(capIncSetter), setArgs, span));

            int propRetId = (!wantResult || !incDec.IsPostfix) ? propNewTemp : idSetCap.PrimaryLocalId;
            return new MirOperand.CopyOrMove(MirPlace.Local(propRetId), valType, span);
        }

        if (unwrappedTarget is IPropertyReferenceOperation
            {
                Property:
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    GetMethod: { } propGetter,
                    SetMethod: { } propSetter,
                },
            } propRef &&
            propSetter.ContainingType is INamedTypeSymbol propOwner &&
            IsInCompilationOrVibraniumStd(propOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(propGetter);
            var substSetter = SubstituteMethod(propSetter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(propOwner);

            MirPassKind? getRecvPass = null;
            MirPassKind? setRecvPass = null;
            MirPlace? recvPlace = null;
            VibraniumSpan recvSpan = span;

            if (!substSetter.IsStatic && propRef.Instance is not null)
            {
                getRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
                bool needMutRecv = getRecvPass == MirPassKind.ImplicitThisMut || setRecvPass == MirPassKind.ImplicitThisMut;
                var recvMirT = MapMirType(SubstituteType(propRef.Instance.Type));
                recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(propRef.Instance.Syntax);
                recvPlace = needMutRecv
                    ? LowerPlace(propRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(propRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);
            }

            var evalIdxArgs = propRef.Arguments.Length > 0
                ? OrderCallArguments(propRef.Arguments, substGetter.Parameters, span, materializeByValueTemps: true)
                : new List<MirCallArg>();

            var getArgs = new List<MirCallArg>(evalIdxArgs.Count + 1);
            if (getRecvPass is not null && recvPlace is not null)
            {
                getArgs.Add(new MirCallArg(getRecvPass.Value, null, recvPlace, recvSpan));
            }
            AddGetterIndexArgs(getArgs, evalIdxArgs);

            int propOldTemp = CreateTempLocal(valType, span);
            _currentStmts.Add(new MirStatement.Call(
                MirPlace.Local(propOldTemp),
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substGetter),
                getArgs,
                span));

            int propNewTemp = CreateTempLocal(valType, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(propNewTemp),
                new MirRvalue.Binary(binOp, new MirOperand.CopyOrMove(MirPlace.Local(propOldTemp), valType, span), MakeStepConst(), valType, incDec.IsChecked),
                span));
            var newValOp = new MirOperand.CopyOrMove(MirPlace.Local(propNewTemp), valType, span);

            var setArgs = new List<MirCallArg>(evalIdxArgs.Count + 2);
            if (setRecvPass is not null && recvPlace is not null)
            {
                setArgs.Add(new MirCallArg(setRecvPass.Value, null, recvPlace, recvSpan));
            }
            setArgs.AddRange(evalIdxArgs);
            setArgs.Add(new MirCallArg(MirPassKind.ByValue, newValOp, null, span));

            _currentStmts.Add(new MirStatement.Call(
                null,
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substSetter),
                setArgs,
                span));

            int propRetId = (!wantResult || !incDec.IsPostfix) ? propNewTemp : propOldTemp;
            return new MirOperand.CopyOrMove(MirPlace.Local(propRetId), valType, span);
        }

        if (unwrappedTarget is IImplicitIndexerReferenceOperation
            {
                IndexerSymbol: IPropertySymbol
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    GetMethod: { } implGetter,
                    SetMethod: { } implSetter,
                },
                LengthSymbol: { } implLenSym,
            } implRef &&
            SubstituteType(implRef.Argument.Type)?.ToDisplayString() == "System.Index" &&
            implSetter.ContainingType is INamedTypeSymbol implOwner &&
            IsInCompilationOrVibraniumStd(implOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(implGetter);
            var substSetter = SubstituteMethod(implSetter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(implOwner);

            var getRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
            var setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
            bool needMutRecv =
                getRecvPass == MirPassKind.ImplicitThisMut ||
                setRecvPass == MirPassKind.ImplicitThisMut ||
                (IsFromEndIndexOperation(implRef.Argument) && IsLengthSymbolMutating(implLenSym));

            var recvMirT = MapMirType(SubstituteType(implRef.Instance.Type));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implRef.Instance.Syntax);
            var recvPlace = needMutRecv
                ? LowerPlace(implRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(implRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

            var idxOp = SnapshotCopyOperandIfNeeded(
                LowerSystemIndexGeneric(implRef.Argument, recvPlace, implLenSym, span),
                MirType.Int32,
                span);

            int implOldTemp = CreateTempLocal(valType, span);
            var getArgs = new List<MirCallArg>(2)
            {
                new(getRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
            };
            _currentStmts.Add(new MirStatement.Call(
                MirPlace.Local(implOldTemp),
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substGetter),
                getArgs,
                span));

            int implNewTemp = CreateTempLocal(valType, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(implNewTemp),
                new MirRvalue.Binary(binOp, new MirOperand.CopyOrMove(MirPlace.Local(implOldTemp), valType, span), MakeStepConst(), valType, incDec.IsChecked),
                span));
            var implNewOp = new MirOperand.CopyOrMove(MirPlace.Local(implNewTemp), valType, span);

            var setArgs = new List<MirCallArg>(3)
            {
                new(setRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
                new(MirPassKind.ByValue, implNewOp, null, span),
            };
            _currentStmts.Add(new MirStatement.Call(
                null,
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substSetter),
                setArgs,
                span));

            int implRetId = (!wantResult || !incDec.IsPostfix) ? implNewTemp : implOldTemp;
            return new MirOperand.CopyOrMove(MirPlace.Local(implRetId), valType, span);
        }

        var place = LowerPlace(incDec.Target, isMutableAccess: true);

        int oldTemp = GetOrSnapshotUpdateOldValue(incDec.Target, place, valType, span);

        int newTemp = CreateTempLocal(valType, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(newTemp),
            new MirRvalue.Binary(binOp, new MirOperand.CopyOrMove(MirPlace.Local(oldTemp), valType, span), MakeStepConst(), valType, incDec.IsChecked),
            span));

        _currentStmts.Add(new MirStatement.Assign(
            place,
            new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(newTemp), valType, span)),
            span));

        if (!wantResult)
        {
            return new MirOperand.CopyOrMove(MirPlace.Local(newTemp), valType, span);
        }

        int retId = incDec.IsPostfix ? oldTemp : newTemp;
        return new MirOperand.CopyOrMove(MirPlace.Local(retId), valType, span);
    }

    private static bool IsGateRejectedNullLiteral(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null } && operation.Syntax.IsKind(SyntaxKind.NullLiteralExpression);

    private MirRvalue LowerRvalue(IOperation op, MirType expectedType)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax);

        if (IsGateRejectedNullLiteral(op))
        {
            // The syntax gate already reports VIB008 at this literal.
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
        }

        if (op is IFieldReferenceOperation && op.Syntax is MemberBindingExpressionSyntax)
        {
            // A ?. field access is already rejected by the gate. Its synthetic value
            // capture is not a source-level move out of the borrowed owner's field.
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
        }

        if (op is ISizeOfOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'sizeof(...)' expressions are not supported in Vibranium.", span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
        }
        if (op is INameOfOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'nameof(...)' expressions are not supported in Vibranium.", span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
        }
        if (op is IWithOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'with' expressions are not supported in Vibranium.", span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
        }

        if (op.ConstantValue.HasValue && TryMakeConstantOperand(op.ConstantValue.Value, SubstituteType(op.Type), expectedType, span, out var constOp))
        {
            return new MirRvalue.Use(constOp);
        }

        if (TryLowerStackAllocRvalue(op, expectedType, span, out var stackAllocRv))
        {
            return stackAllocRv;
        }

        switch (op)
        {
            case IUtf8StringOperation utf8Op:
            {
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(utf8Op.Value);
                var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.Utf8BytesConst(utf8Bytes), u8SpanType, span));
            }

            case IConversionOperation conv:
                return LowerConversionRvalue(conv, expectedType, span);

            case ICollectionExpressionOperation colExpr:
                return LowerCollectionExpressionRvalue(colExpr, expectedType, span);

            case IBinaryOperation bin:
            {
                if (_loweringRawFieldInitializer && bin.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr)
                {
                    // A raw tree has no CFG edge to skip the right operand. Never lower it as eager bitwise logic.
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, "Short-circuit operation reached raw field initializer lowering.", span);
                    return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
                }
                if (bin.Type?.SpecialType == SpecialType.System_String && bin.OperatorKind == BinaryOperatorKind.Add)
                {
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, "Runtime string concatenation is not supported in Vibranium.", span);
                    return new MirRvalue.Use(new MirOperand.Const(new MirConstant.StringConst(string.Empty), MirType.StringLiteral, span));
                }

                var resType = MapMirType(SubstituteType(bin.Type));
                if (bin.OperatorMethod is not null)
                {
                    return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(resType), resType, span));
                }

                if (TryExtractSwitchCaseNaNScrutinee(bin, out var nanScrutOp, out var nanScrutType))
                {
                    var scrutOperand = LowerOperand(nanScrutOp, nanScrutType);
                    return new MirRvalue.Use(EmitIsNaNTest(scrutOperand, nanScrutType, span));
                }

                var leftType = MapMirType(SubstituteType(bin.LeftOperand.Type));
                var rightType = MapMirType(SubstituteType(bin.RightOperand.Type));
                var left = LowerOperand(bin.LeftOperand, leftType);
                var right = LowerOperand(bin.RightOperand, rightType);
                return new MirRvalue.Binary(MapBinaryOp(bin.OperatorKind), left, right, resType, bin.IsChecked);
            }

            case IUnaryOperation un:
            {
                var resType = MapMirType(SubstituteType(un.Type));
                if (un.OperatorMethod is not null)
                {
                    return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(resType), resType, span));
                }
                var operandType = MapMirType(SubstituteType(un.Operand.Type));
                var inner = LowerOperand(un.Operand, operandType);
                if (un.OperatorKind == UnaryOperatorKind.Plus)
                {
                    return new MirRvalue.Use(inner);
                }
                var unKind = un.OperatorKind switch
                {
                    UnaryOperatorKind.Minus => MirUnaryOp.Neg,
                    UnaryOperatorKind.Not => MirUnaryOp.Not,
                    UnaryOperatorKind.BitwiseNegation => MirUnaryOp.BitNot,
                    _ => MirUnaryOp.Neg,
                };
                return new MirRvalue.Unary(unKind, inner, resType, un.IsChecked);
            }

            case IObjectCreationOperation objCreate:
                return LowerObjectCreationRvalue(objCreate, expectedType, span);

            case ITypeParameterObjectCreationOperation tpObjCreate:
            {
                var tpType = MapMirType(SubstituteType(tpObjCreate.Type));
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(tpType), tpType, span));
            }

            case IDefaultValueOperation defOp:
            {
                var t = MapMirType(SubstituteType(defOp.Type));
                if (t is MirType.OptionType opt)
                {
                    return new MirRvalue.LiftOptionNone(opt.Inner);
                }
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(t), t, span));
            }

            case IPropertyReferenceOperation propRef when
                propRef.Property.Name == "None" &&
                propRef.Property.ContainingType?.Name == "Prelude" &&
                propRef.Property.ContainingNamespace?.ToDisplayString() == "Vibranium":
            {
                if (expectedType is MirType.OptionType opt)
                {
                    return new MirRvalue.LiftOptionNone(opt.Inner);
                }
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.NoneUnit), MirType.NoneUnit, span));
            }

            case IFieldReferenceOperation fieldRef:
            {
                if (fieldRef.Field.ContainingType is INamedTypeSymbol fOwner)
                {
                    if (VibraniumSymbolHelpers.IsVibraniumCoreType(fOwner, "Some") && fieldRef.Field.Name == "Value" && fieldRef.Instance is not null)
                    {
                        var innerT = MapMirType(SubstituteType(fOwner.TypeArguments[0]));
                        var wrapOp = NoteWrapperRead(fieldRef, LowerOperand(fieldRef.Instance, new MirType.SomeWrapperType(innerT)));
                        return new MirRvalue.UnwrapSomeWrapper(innerT, wrapOp);
                    }
                    if (VibraniumSymbolHelpers.IsVibraniumCoreType(fOwner, "Ok") && fieldRef.Field.Name == "Value" && fieldRef.Instance is not null)
                    {
                        var okT = MapMirType(SubstituteType(fOwner.TypeArguments[0]));
                        var wrapOp = NoteWrapperRead(fieldRef, LowerOperand(fieldRef.Instance, new MirType.OkWrapperType(okT)));
                        return new MirRvalue.UnwrapOkWrapper(okT, wrapOp);
                    }
                    if (VibraniumSymbolHelpers.IsVibraniumCoreType(fOwner, "Err") && fieldRef.Field.Name == "Error" && fieldRef.Instance is not null)
                    {
                        var errT = MapMirType(SubstituteType(fOwner.TypeArguments[0]));
                        var wrapOp = NoteWrapperRead(fieldRef, LowerOperand(fieldRef.Instance, new MirType.ErrWrapperType(errT)));
                        return new MirRvalue.UnwrapErrWrapper(errT, wrapOp);
                    }
                }
                if (fieldRef.Field.IsConst && fieldRef.Field.HasConstantValue &&
                    TryMakeConstantOperand(fieldRef.Field.ConstantValue, SubstituteType(fieldRef.Type), expectedType, span, out var fcOp))
                {
                    return new MirRvalue.Use(fcOp);
                }
                return new MirRvalue.Use(new MirOperand.CopyOrMove(LowerPlace(fieldRef, isMutableAccess: false), MapMirType(SubstituteType(fieldRef.Type)), span));
            }

            case ISimpleAssignmentOperation sa:
            {
                var res = LowerSimpleAssignment(sa, wantResult: true);
                if (res is not null) return new MirRvalue.Use(res);
                break;
            }

            case ICompoundAssignmentOperation ca:
            {
                var res = LowerCompoundAssignment(ca, wantResult: true);
                if (res is not null) return new MirRvalue.Use(res);
                break;
            }

            case IIncrementOrDecrementOperation incDec:
                return new MirRvalue.Use(LowerIncrementOrDecrement(incDec, wantResult: true));

            case IIsPatternOperation isPat:
                return new MirRvalue.Use(LowerIsPatternExpression(isPat, span));

            case ILocalReferenceOperation:
            case IParameterReferenceOperation:
            case IInstanceReferenceOperation:
            case IFlowCaptureReferenceOperation:
            case IInvocationOperation:
                return new MirRvalue.Use(LowerOperand(op, expectedType));

            case IPropertyReferenceOperation propLeaf:
            {
                var pType = propLeaf.Type is not null ? MapMirType(SubstituteType(propLeaf.Type)) : expectedType;
                int tmp = CreateTempLocal(pType, span);
                if (TryEmitPropertyReferenceInto(propLeaf, MirPlace.Local(tmp), pType, span))
                {
                    return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), pType, span));
                }
                // Unsupported property (e.g. [Obsolete] union protocol property already flagged by Gatekeeper)
                if (!(propLeaf.Property.ContainingNamespace?.ToDisplayString() == "Vibranium" && VibraniumSymbolHelpers.HasObsoleteAttribute(propLeaf.Property)))
                {
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Property '{propLeaf.Property.Name}' is not supported in Vibranium.", span);
                }
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(pType), pType, span));
            }

            case IImplicitIndexerReferenceOperation implLeaf:
            {
                var iType = implLeaf.Type is not null ? MapMirType(SubstituteType(implLeaf.Type)) : expectedType;
                int tmp = CreateTempLocal(iType, span);
                if (TryEmitImplicitRangeOrIndexInto(implLeaf, MirPlace.Local(tmp), iType, span))
                {
                    return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), iType, span));
                }
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Indexer expression is not supported in Vibranium.", span);
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(iType), iType, span));
            }
        }

        if (op is not IInvalidOperation and not IDelegateCreationOperation and not IAnonymousFunctionOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Expression '{op.Kind}' is not supported in Vibranium.", span);
        }
        return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
    }

    private MirOperand LowerOperand(IOperation op, MirType expectedType)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax);

        if (IsGateRejectedNullLiteral(op))
        {
            // The syntax gate already reports VIB008 at this literal.
            return new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span);
        }

        if (op is IFieldReferenceOperation && op.Syntax is MemberBindingExpressionSyntax)
        {
            // A ?. field access is already rejected by the gate. Its synthetic value
            // capture is not a source-level move out of the borrowed owner's field.
            return new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span);
        }

        if (op is ISizeOfOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'sizeof(...)' expressions are not supported in Vibranium.", span);
            return new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span);
        }
        if (op is INameOfOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'nameof(...)' expressions are not supported in Vibranium.", span);
            return new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span);
        }
        if (op is IWithOperation)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "'with' expressions are not supported in Vibranium.", span);
            return new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span);
        }

        if (op.ConstantValue.HasValue && TryMakeConstantOperand(op.ConstantValue.Value, SubstituteType(op.Type), expectedType, span, out var constOp))
        {
            return constOp;
        }

        int tempId;
        switch (op)
        {
            case IIsPatternOperation isPat:
                return LowerIsPatternExpression(isPat, span);
            case IUtf8StringOperation utf8Lit:
            {
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(utf8Lit.Value);
                var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                return new MirOperand.Const(new MirConstant.Utf8BytesConst(utf8Bytes), u8SpanType, span);
            }

            case ILocalReferenceOperation lr:
            {
                var place = LowerPlace(lr, isMutableAccess: false);
                var t = MapMirType(SubstituteType(lr.Type));
                return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(place, t, span), t, span);
            }

            case IParameterReferenceOperation pr:
            {
                var place = LowerPlace(pr, isMutableAccess: false);
                var t = MapMirType(SubstituteType(pr.Type));
                return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(place, t, span), t, span);
            }

            case IInstanceReferenceOperation ir:
            {
                var place = LowerPlace(ir, isMutableAccess: false);
                var t = MapMirType(SubstituteType(ir.Type));
                return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(place, t, span), t, span);
            }

            case IFieldReferenceOperation fr when
                fr.Field.ContainingType is not null &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Some") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Ok") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Err"):
            {
                if (fr.Field.IsConst && fr.Field.HasConstantValue &&
                    TryMakeConstantOperand(fr.Field.ConstantValue, SubstituteType(fr.Type), expectedType, span, out var fieldConst))
                {
                    return fieldConst;
                }
                var place = LowerPlace(fr, isMutableAccess: false);
                var t = MapMirType(SubstituteType(fr.Type));
                return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(place, t, span), t, span);
            }

            case IFlowCaptureReferenceOperation fcr:
            {
                var place = LowerPlace(fcr, isMutableAccess: false);
                var t = MapMirType(SubstituteType(fcr.Type));
                return SnapshotCopyOperandIfNeeded(new MirOperand.CopyOrMove(place, t, span), t, span);
            }

            case IInvocationOperation inv:
            {
                var retMirType = MapMirType(SubstituteType(inv.Type));
                if (retMirType is MirType.VoidType)
                {
                    _ = LowerInvocation(inv, destPlace: null);
                    return new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
                }
                tempId = CreateTempLocal(retMirType, span);
                var dest = MirPlace.Local(tempId);
                var res = LowerInvocation(inv, dest);
                if (res is not null && !PlacedIntoDest(res, dest))
                {
                    _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.Use(res), span));
                }
                else if (res is null && !_module.IsLinearResource(retMirType))
                {
                    _currentStmts.Add(new MirStatement.Assign(
                        dest,
                        new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(retMirType), retMirType, span)),
                        span));
                }
                return new MirOperand.CopyOrMove(dest, retMirType, span);
            }

            default:
            {
                var valType = op.Type is not null ? MapMirType(SubstituteType(op.Type)) : expectedType;
                tempId = CreateTempLocal(valType, span);
                var destPlace = MirPlace.Local(tempId);
                if (!TryEmitSpecialIntrinsicExpressionInto(op, destPlace, valType, span))
                {
                    if (IsDirectRvalueHandled(op))
                    {
                        var rv = LowerRvalue(op, valType);
                        _currentStmts.Add(new MirStatement.Assign(destPlace, rv, span));
                    }
                    else
                    {
                        // Unsupported expression (e.g. lambda/delegate/query/invalid): Gatekeeper reports VIB000 if not already reported
                        if (op is not IInvalidOperation &&
                            op is not IDelegateCreationOperation &&
                            op is not IAnonymousFunctionOperation)
                        {
                            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Expression '{op.Kind}' is not supported in Vibranium.", span);
                        }
                        _currentStmts.Add(new MirStatement.Assign(
                            destPlace,
                            new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(valType), valType, span)),
                            span));
                    }
                }
                return new MirOperand.CopyOrMove(destPlace, valType, span);
            }
        }
    }

    private MirOperand SnapshotCopyOperandIfNeeded(MirOperand operand, MirType type, VibraniumSpan span)
    {
        if (operand is MirOperand.CopyOrMove cm &&
            _module.IsCopy(type) &&
            (cm.Place.Projections.Count > 0 || (cm.Place.LocalId >= 0 && cm.Place.LocalId < _locals.Count && !_locals[cm.Place.LocalId].IsCompilerTemp)))
        {
            int snapTemp = CreateTempLocal(type, span, "__snap");
            _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(snapTemp), new MirRvalue.Use(operand), span));
            return new MirOperand.CopyOrMove(MirPlace.Local(snapTemp), type, span);
        }
        return operand;
    }

    private static bool IsDirectRvalueHandled(IOperation op) =>
        op is IUtf8StringOperation or
              IConversionOperation or
              ICollectionExpressionOperation or
              IBinaryOperation or
              IUnaryOperation or
              IObjectCreationOperation or
              IDefaultValueOperation or
              IPropertyReferenceOperation or
              IFieldReferenceOperation or
              ISimpleAssignmentOperation or
              ICompoundAssignmentOperation or
              IIncrementOrDecrementOperation or
              IIsPatternOperation ||
        op.Syntax is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax;

    private bool TryEmitSpecialIntrinsicExpressionInto(IOperation op, MirPlace destPlace, MirType targetMirType, VibraniumSpan span)
    {
        switch (op)
        {
            case IConversionOperation conv:
            {
                var srcType = SubstituteType(conv.Operand.Type);
                var dstType = SubstituteType(conv.Type);

                if (conv.Operand is ICollectionExpressionOperation colExpr)
                {
                    EmitCollectionExpressionInto(colExpr, destPlace, targetMirType, span);
                    return true;
                }

                if (VibraniumSymbolHelpers.IsVibraniumCoreType(srcType, "Vec") && VibraniumSymbolHelpers.IsSpanType(dstType, out _, out bool dstRo))
                {
                    var vecPlace = LowerPlace(conv.Operand, isMutableAccess: !dstRo);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        dstRo ? MirIntrinsicKind.VecAsReadOnlySpan : MirIntrinsicKind.VecAsSpan,
                        destPlace,
                        null,
                        false,
                        Array.Empty<MirOperand>(),
                        new[] { vecPlace },
                        targetMirType,
                        span));
                    return true;
                }

                if (VibraniumSymbolHelpers.IsSpanType(srcType, out _, out bool srcRo) &&
                    VibraniumSymbolHelpers.IsSpanType(dstType, out _, out bool dstRo2) &&
                    !srcRo && dstRo2)
                {
                    var spanPlace = LowerPlaceOrMaterializeTemp(conv.Operand, MapMirType(srcType), isMutableAccess: false, span);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.SpanToReadOnly,
                        destPlace,
                        null,
                        false,
                        Array.Empty<MirOperand>(),
                        new[] { spanPlace },
                        targetMirType,
                        span));
                    return true;
                }

                return false;
            }

            case ICollectionExpressionOperation colDirect:
                EmitCollectionExpressionInto(colDirect, destPlace, targetMirType, span);
                return true;

            case IPropertyReferenceOperation prop:
                return TryEmitPropertyReferenceInto(prop, destPlace, targetMirType, span);

            case IImplicitIndexerReferenceOperation implIdx:
                return TryEmitImplicitRangeOrIndexInto(implIdx, destPlace, targetMirType, span);

            case IObjectCreationOperation utf8EnumCreate when
                VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(utf8EnumCreate.Type), "Utf8ArgEnumerator") &&
                utf8EnumCreate.Arguments.Length == 1:
            {
                var roSpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                var srcOp = LowerOperand(utf8EnumCreate.Arguments[0].Value, roSpanType);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanToUtf8View,
                    destPlace,
                    null,
                    false,
                    new[] { srcOp },
                    Array.Empty<MirPlace>(),
                    MirType.Utf8ArgEnumerator,
                    span));
                return true;
            }

            case IObjectCreationOperation objCreate when VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(objCreate.Type), "Vec"):
            {
                if (objCreate.Arguments.Length == 1)
                {
                    var capOp = LowerOperand(objCreate.Arguments[0].Value, MirType.Int32);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.VecNewWithCapacity,
                        destPlace,
                        null,
                        false,
                        new[] { capOp },
                        Array.Empty<MirPlace>(),
                        targetMirType,
                        span));
                    return true;
                }
                return false;
            }

            case IObjectCreationOperation userObjCreate when
                userObjCreate.Constructor is not null &&
                !userObjCreate.Constructor.IsImplicitlyDeclared &&
                SubstituteType(userObjCreate.Type) is INamedTypeSymbol constructedType &&
                constructedType.TypeKind is TypeKind.Class or TypeKind.Struct &&
                constructedType.ContainingNamespace?.ToDisplayString() != "Vibranium" &&
                IsInCompilationOrVibraniumStd(constructedType.ContainingAssembly):
            {
                EmitConstructorCall(userObjCreate, constructedType, destPlace, span);
                return true;
            }

            case IObjectCreationOperation implicitClassCtor when
                implicitClassCtor.Constructor is { IsImplicitlyDeclared: true } &&
                SubstituteType(implicitClassCtor.Type) is INamedTypeSymbol clsType &&
                clsType.TypeKind == TypeKind.Class &&
                clsType.ContainingNamespace?.ToDisplayString() != "Vibranium" &&
                IsInCompilationOrVibraniumStd(clsType.ContainingAssembly):
            {
                EmitConstructorCall(implicitClassCtor, clsType, destPlace, span);
                return true;
            }

            default:
                return false;
        }
    }

    private void EmitCollectionExpressionInto(
        ICollectionExpressionOperation colExpr,
        MirPlace destPlace,
        MirType targetMirType,
        VibraniumSpan span)
    {
        if (targetMirType is MirType.VecType vecT)
        {
            var elems = LowerCollectionElements(colExpr, vecT.Element, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.VecFromLiteral,
                destPlace,
                null,
                false,
                elems,
                Array.Empty<MirPlace>(),
                vecT,
                span));
            return;
        }

        if (targetMirType is MirType.SpanType spanT)
        {
            var elems = LowerCollectionElements(colExpr, spanT.Element, span);
            _currentStmts.Add(new MirStatement.Assign(
                destPlace,
                new MirRvalue.StackSpan(spanT.Element, spanT.IsReadOnly, elems, ZeroInitLength: 0),
                span));
            return;
        }

        EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Collection expression for '{targetMirType}' is not supported in Vibranium.", span);
    }

    private MirRvalue LowerCollectionExpressionRvalue(
        ICollectionExpressionOperation colExpr,
        MirType expectedType,
        VibraniumSpan span)
    {
        if (expectedType is MirType.SpanType spanT)
        {
            var elems = LowerCollectionElements(colExpr, spanT.Element, span);
            return new MirRvalue.StackSpan(spanT.Element, spanT.IsReadOnly, elems, ZeroInitLength: 0);
        }

        int tmp = CreateTempLocal(expectedType, span);
        EmitCollectionExpressionInto(colExpr, MirPlace.Local(tmp), expectedType, span);
        return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), expectedType, span));
    }

    private List<MirOperand> LowerCollectionElements(
        ICollectionExpressionOperation colExpr,
        MirType elemType,
        VibraniumSpan span)
    {
        var list = new List<MirOperand>(colExpr.Elements.Length);
        foreach (var el in colExpr.Elements)
        {
            if (el is ISpreadOperation)
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Collection spread '..' elements are not supported in Vibranium.", span);
                continue;
            }
            list.Add(LowerOperand(el, elemType));
        }
        return list;
    }

    private bool TryLowerStackAllocRvalue(
        IOperation op,
        MirType expectedType,
        VibraniumSpan span,
        out MirRvalue rvalue)
    {
        rvalue = null!;
        var unwrapped = op;
        if (unwrapped is IConversionOperation conv &&
            (conv.Operand.Syntax is StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax))
        {
            unwrapped = conv.Operand;
        }

        if (unwrapped.Syntax is not StackAllocArrayCreationExpressionSyntax and not ImplicitStackAllocArrayCreationExpressionSyntax)
        {
            return false;
        }

        MirType elemMirType = expectedType is MirType.SpanType st ? st.Element : MirType.Int32;
        bool isReadOnly = expectedType is MirType.SpanType { IsReadOnly: true };

        InitializerExpressionSyntax? initSyntax = unwrapped.Syntax switch
        {
            StackAllocArrayCreationExpressionSyntax sa => sa.Initializer,
            ImplicitStackAllocArrayCreationExpressionSyntax isa => isa.Initializer,
            _ => null,
        };

        if (initSyntax is not null)
        {
            var model = _compilation.GetSemanticModel(initSyntax.SyntaxTree);
            var elemOps = new List<MirOperand>(initSyntax.Expressions.Count);
            foreach (var expr in initSyntax.Expressions)
            {
                var elemIOp = model.GetOperation(expr);
                if (elemIOp is not null)
                {
                    elemOps.Add(LowerOperand(elemIOp, elemMirType));
                }
            }
            rvalue = new MirRvalue.StackSpan(elemMirType, isReadOnly, elemOps, ZeroInitLength: 0);
            return true;
        }

        int constLen = 0;
        var childLenOp = unwrapped.ChildOperations.FirstOrDefault();
        if (childLenOp is not null && childLenOp.ConstantValue.HasValue && childLenOp.ConstantValue.Value is int n && n >= 0)
        {
            constLen = n;
        }
        else if (unwrapped.Syntax is StackAllocArrayCreationExpressionSyntax { Type: ArrayTypeSyntax arrT } &&
                 arrT.RankSpecifiers.Count > 0 &&
                 arrT.RankSpecifiers[0].Sizes.Count == 1)
        {
            var sizeSyntax = arrT.RankSpecifiers[0].Sizes[0];
            var model = _compilation.GetSemanticModel(sizeSyntax.SyntaxTree);
            var cv = model.GetConstantValue(sizeSyntax);
            if (cv.HasValue && cv.Value is int cn && cn >= 0)
            {
                constLen = cn;
            }
            else
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Dynamic-length stackalloc is not supported in Vibranium; use a compile-time constant length or Vec<T>.", span);
            }
        }

        rvalue = new MirRvalue.StackSpan(elemMirType, isReadOnly, Array.Empty<MirOperand>(), ZeroInitLength: constLen);
        return true;
    }

    private MirRvalue LowerConversionRvalue(IConversionOperation conv, MirType expectedType, VibraniumSpan span)
    {
        if (conv.Operand is ICollectionExpressionOperation colExpr)
        {
            return LowerCollectionExpressionRvalue(colExpr, expectedType, span);
        }

        if (TryLowerStackAllocRvalue(conv, expectedType, span, out var stackRv))
        {
            return stackRv;
        }

        var srcTypeSym = SubstituteType(conv.Operand.Type);
        var dstTypeSym = SubstituteType(conv.Type);
        var dstMirType = dstTypeSym is not null ? MapMirType(dstTypeSym) : expectedType;

        if (dstMirType is MirType.OptionType optDst)
        {
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(srcTypeSym, "NoneType"))
            {
                return new MirRvalue.LiftOptionNone(optDst.Inner);
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(srcTypeSym, "Some"))
            {
                if (TryExtractDirectWrapperArgument(conv.Operand, "Some", optDst.Inner, out var directSomePayload))
                {
                    return new MirRvalue.LiftOptionSome(optDst.Inner, directSomePayload);
                }
                var wrapperOp = LowerOperand(conv.Operand, new MirType.SomeWrapperType(optDst.Inner));
                int unwrappedTemp = CreateTempLocal(optDst.Inner, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(unwrappedTemp),
                    new MirRvalue.UnwrapSomeWrapper(optDst.Inner, wrapperOp),
                    span));
                return new MirRvalue.LiftOptionSome(
                    optDst.Inner,
                    new MirOperand.CopyOrMove(MirPlace.Local(unwrappedTemp), optDst.Inner, span));
            }
        }

        if (dstMirType is MirType.ResultType resDst)
        {
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(srcTypeSym, "Ok"))
            {
                if (TryExtractDirectWrapperArgument(conv.Operand, "Ok", resDst.Ok, out var directOkPayload))
                {
                    return new MirRvalue.LiftResultOk(resDst.Ok, resDst.Err, directOkPayload);
                }
                var wrapperOp = LowerOperand(conv.Operand, new MirType.OkWrapperType(resDst.Ok));
                int unwrappedTemp = CreateTempLocal(resDst.Ok, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(unwrappedTemp),
                    new MirRvalue.UnwrapOkWrapper(resDst.Ok, wrapperOp),
                    span));
                return new MirRvalue.LiftResultOk(
                    resDst.Ok,
                    resDst.Err,
                    new MirOperand.CopyOrMove(MirPlace.Local(unwrappedTemp), resDst.Ok, span));
            }

            if (VibraniumSymbolHelpers.IsVibraniumCoreType(srcTypeSym, "Err"))
            {
                if (TryExtractDirectWrapperArgument(conv.Operand, "Err", resDst.Err, out var directErrPayload))
                {
                    return new MirRvalue.LiftResultErr(resDst.Ok, resDst.Err, directErrPayload);
                }
                var wrapperOp = LowerOperand(conv.Operand, new MirType.ErrWrapperType(resDst.Err));
                int unwrappedTemp = CreateTempLocal(resDst.Err, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(unwrappedTemp),
                    new MirRvalue.UnwrapErrWrapper(resDst.Err, wrapperOp),
                    span));
                return new MirRvalue.LiftResultErr(
                    resDst.Ok,
                    resDst.Err,
                    new MirOperand.CopyOrMove(MirPlace.Local(unwrappedTemp), resDst.Err, span));
            }
        }

        if ((VibraniumSymbolHelpers.IsVibraniumCoreType(srcTypeSym, "Vec") && VibraniumSymbolHelpers.IsSpanType(dstTypeSym, out _, out _)) ||
            (VibraniumSymbolHelpers.IsSpanType(srcTypeSym, out _, out bool sRo) && VibraniumSymbolHelpers.IsSpanType(dstTypeSym, out _, out bool dRo) && !sRo && dRo))
        {
            int tmp = CreateTempLocal(dstMirType, span);
            _ = TryEmitSpecialIntrinsicExpressionInto(conv, MirPlace.Local(tmp), dstMirType, span);
            return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), dstMirType, span));
        }

        if (srcTypeSym?.SpecialType == SpecialType.System_String && VibraniumSymbolHelpers.IsSpanType(dstTypeSym, out _, out _))
        {
            EmitDiag(
                VibraniumDiagnosticIds.Unsupported,
                "Conversion from 'string' to 'ReadOnlySpan<char>' is banned in Vibranium; use '.AsUtf8()' for UTF-8 bytes.",
                span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(dstMirType), dstMirType, span));
        }

        if (dstTypeSym is not null && (dstTypeSym.SpecialType == SpecialType.System_Object || dstTypeSym.TypeKind == TypeKind.Interface))
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Boxing or interface conversion to '{dstTypeSym.ToDisplayString()}' is banned in Vibranium.", span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span));
        }

        if (conv.Conversion.MethodSymbol is { MethodKind: MethodKind.Conversion })
        {
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(dstMirType), dstMirType, span));
        }

        var srcMirType = srcTypeSym is not null ? MapMirType(srcTypeSym) : dstMirType;
        if (Equals(srcMirType, dstMirType))
        {
            return LowerRvalue(conv.Operand, dstMirType);
        }

        if (!IsScalarCastableMirType(srcMirType) || !IsScalarCastableMirType(dstMirType))
        {
            EmitDiag(
                VibraniumDiagnosticIds.Unsupported,
                $"Conversion from '{srcTypeSym?.ToDisplayString() ?? srcMirType.ToString()}' to '{dstTypeSym?.ToDisplayString() ?? dstMirType.ToString()}' is not supported in Vibranium.",
                span);
            return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(dstMirType), dstMirType, span));
        }

        var innerOperand = LowerOperand(conv.Operand, srcMirType);
        return new MirRvalue.Cast(innerOperand, dstMirType, conv.IsChecked);
    }

    private bool TryExtractDirectWrapperArgument(
        IOperation wrapperOp,
        string wrapperName,
        MirType payloadType,
        out MirOperand payloadOperand)
    {
        payloadOperand = null!;
        var unwrapped = UnwrapImplicitIdentityConversion(wrapperOp);
        if (unwrapped is IInvocationOperation inv &&
            inv.TargetMethod.Name == wrapperName &&
            inv.TargetMethod.ContainingType?.Name == "Prelude")
        {
            if (inv.Arguments.Length == 1)
            {
                payloadOperand = LowerOperand(inv.Arguments[0].Value, payloadType);
                return true;
            }
            if (wrapperName == "Ok" && inv.Arguments.Length == 0 && inv.TargetMethod.TypeArguments.Length == 0)
            {
                var okUnitSpan = VibraniumSymbolHelpers.ToVibraniumSpan(inv.Syntax);
                payloadOperand = new MirOperand.Const(new MirConstant.ZeroDefault(payloadType), payloadType, okUnitSpan);
                return true;
            }
        }
        if (unwrapped is IObjectCreationOperation oc &&
            VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(oc.Type), wrapperName) &&
            oc.Arguments.Length == 1)
        {
            payloadOperand = LowerOperand(oc.Arguments[0].Value, payloadType);
            return true;
        }
        return false;
    }

    private MirRvalue LowerObjectCreationRvalue(IObjectCreationOperation objCreate, MirType expectedType, VibraniumSpan span)
    {
        var typeSym = SubstituteType(objCreate.Type);
        if (typeSym is INamedTypeSymbol named)
        {
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Some") && objCreate.Arguments.Length == 1)
            {
                var innerT = MapMirType(SubstituteType(named.TypeArguments[0]));
                return new MirRvalue.MakeSomeWrapper(innerT, LowerOperand(objCreate.Arguments[0].Value, innerT));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Ok") && objCreate.Arguments.Length == 1)
            {
                var okT = MapMirType(SubstituteType(named.TypeArguments[0]));
                return new MirRvalue.MakeOkWrapper(okT, LowerOperand(objCreate.Arguments[0].Value, okT));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Err") && objCreate.Arguments.Length == 1)
            {
                var errT = MapMirType(SubstituteType(named.TypeArguments[0]));
                return new MirRvalue.MakeErrWrapper(errT, LowerOperand(objCreate.Arguments[0].Value, errT));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "NoneType") || VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Unit"))
            {
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.NoneUnit), MirType.NoneUnit, span));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Option"))
            {
                var innerT = MapMirType(SubstituteType(named.TypeArguments[0]));
                if (objCreate.Arguments.Length == 0)
                {
                    return new MirRvalue.LiftOptionNone(innerT);
                }
                var argType = SubstituteType(objCreate.Arguments[0].Value.Type);
                if (VibraniumSymbolHelpers.IsVibraniumCoreType(argType, "NoneType"))
                {
                    return new MirRvalue.LiftOptionNone(innerT);
                }
                if (TryExtractDirectWrapperArgument(objCreate.Arguments[0].Value, "Some", innerT, out var pOp))
                {
                    return new MirRvalue.LiftOptionSome(innerT, pOp);
                }
                var someWrap = LowerOperand(objCreate.Arguments[0].Value, new MirType.SomeWrapperType(innerT));
                int tId = CreateTempLocal(innerT, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(tId), new MirRvalue.UnwrapSomeWrapper(innerT, someWrap), span));
                return new MirRvalue.LiftOptionSome(innerT, new MirOperand.CopyOrMove(MirPlace.Local(tId), innerT, span));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Result") && named.TypeArguments.Length == 2 && objCreate.Arguments.Length == 1)
            {
                var okT = MapMirType(SubstituteType(named.TypeArguments[0]));
                var errT = MapMirType(SubstituteType(named.TypeArguments[1]));
                var argType = SubstituteType(objCreate.Arguments[0].Value.Type);
                if (VibraniumSymbolHelpers.IsVibraniumCoreType(argType, "Ok"))
                {
                    if (TryExtractDirectWrapperArgument(objCreate.Arguments[0].Value, "Ok", okT, out var okPayload))
                    {
                        return new MirRvalue.LiftResultOk(okT, errT, okPayload);
                    }
                    var w = LowerOperand(objCreate.Arguments[0].Value, new MirType.OkWrapperType(okT));
                    int tId = CreateTempLocal(okT, span);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(tId), new MirRvalue.UnwrapOkWrapper(okT, w), span));
                    return new MirRvalue.LiftResultOk(okT, errT, new MirOperand.CopyOrMove(MirPlace.Local(tId), okT, span));
                }
                if (VibraniumSymbolHelpers.IsVibraniumCoreType(argType, "Err"))
                {
                    if (TryExtractDirectWrapperArgument(objCreate.Arguments[0].Value, "Err", errT, out var errPayload))
                    {
                        return new MirRvalue.LiftResultErr(okT, errT, errPayload);
                    }
                    var w = LowerOperand(objCreate.Arguments[0].Value, new MirType.ErrWrapperType(errT));
                    int tId = CreateTempLocal(errT, span);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(tId), new MirRvalue.UnwrapErrWrapper(errT, w), span));
                    return new MirRvalue.LiftResultErr(okT, errT, new MirOperand.CopyOrMove(MirPlace.Local(tId), errT, span));
                }
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Utf8ArgEnumerator"))
            {
                if (objCreate.Arguments.Length == 1)
                {
                    var roSpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                    var srcOp = LowerOperand(objCreate.Arguments[0].Value, roSpanType);
                    int tmp = CreateTempLocal(MirType.Utf8ArgEnumerator, span);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.SpanToUtf8View,
                        MirPlace.Local(tmp),
                        null,
                        false,
                        new[] { srcOp },
                        Array.Empty<MirPlace>(),
                        MirType.Utf8ArgEnumerator,
                        span));
                    return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), MirType.Utf8ArgEnumerator, span));
                }
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.Utf8ArgEnumerator), MirType.Utf8ArgEnumerator, span));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Utf8Lines"))
            {
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.Utf8Lines), MirType.Utf8Lines, span));
            }
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(named, "Vec"))
            {
                var vecT = MapMirType(named);
                int tmp = CreateTempLocal(vecT, span);
                if (objCreate.Arguments.Length == 1)
                {
                    var capOp = LowerOperand(objCreate.Arguments[0].Value, MirType.Int32);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.VecNewWithCapacity,
                        MirPlace.Local(tmp),
                        null,
                        false,
                        new[] { capOp },
                        Array.Empty<MirPlace>(),
                        vecT,
                        span));
                    return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(tmp), vecT, span));
                }
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(vecT), vecT, span));
            }

            if (named.TypeKind == TypeKind.Struct &&
                (objCreate.Constructor is null || objCreate.Constructor.IsImplicitlyDeclared) &&
                objCreate.Arguments.Length == 0)
            {
                var stMirType = MapMirType(named);
                _enqueueTypeInstantiation(named);
                return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(stMirType), stMirType, span));
            }

            if (named.TypeKind is TypeKind.Class or TypeKind.Struct && objCreate.Constructor is not null)
            {
                var mirType = MapMirType(named);
                int destTemp = CreateTempLocal(mirType, span);
                EmitConstructorCall(objCreate, named, MirPlace.Local(destTemp), span);
                return new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(destTemp), mirType, span));
            }
        }

        EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Object creation '{objCreate.Syntax}' is not supported in Vibranium.", span);
        return new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(expectedType), expectedType, span));
    }

    private void EmitConstructorCall(
        IObjectCreationOperation objCreate,
        INamedTypeSymbol constructedType,
        MirPlace destPlace,
        VibraniumSpan span)
    {
        _enqueueTypeInstantiation(constructedType);
        var ctorSym = SubstituteMethod(objCreate.Constructor!);
        _enqueueMethodInstantiation(ctorSym);

        var orderedArgs = OrderCallArguments(objCreate.Arguments, ctorSym.Parameters, span);
        string calleeKey = VibraniumSymbolHelpers.GetMethodKey(ctorSym);
        _currentStmts.Add(new MirStatement.Call(destPlace, null, false, calleeKey, orderedArgs, span));
    }

    private MirOperand? LowerInvocation(IInvocationOperation inv, MirPlace? destPlace)
    {
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(inv.Syntax);
        var method = SubstituteMethod(inv.TargetMethod);
        var owner = method.ContainingType;
        string ownerNs = owner?.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        if (method.Name == "Deconstruct")
        {
            if (VibraniumSymbolHelpers.IsLinearResourceSymbol(owner))
            {
                EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "Direct '.Deconstruct(...)' calls on linear union wrappers are banned in Vibranium.", span);
            }
            else
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Direct '.Deconstruct(...)' calls are banned in Vibranium.", span);
            }
            return null;
        }

        if (method.IsGenericMethod &&
            !VibraniumSymbolHelpers.ValidateL1LinearTypeArguments(method, method.TypeArguments, out string l1MethodErr))
        {
            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, l1MethodErr, span);
        }

        if (method.Name == "MoveNext" &&
            inv.Instance is IFlowCaptureReferenceOperation moveNextUtf8Fcr &&
            _captures.TryGetValue(moveNextUtf8Fcr.Id, out var utf8Cap) &&
            utf8Cap.Kind == CaptureKind.Utf8ScanEnumerator)
        {
            var srcOp = new MirOperand.CopyOrMove(MirPlace.Local(utf8Cap.PrimaryLocalId), utf8Cap.MirType, span);
            var posPlace = MirPlace.Local(utf8Cap.SecondaryLocalId);
            var startPlace = MirPlace.Local(utf8Cap.TertiaryLocalId);
            var lenPlace = MirPlace.Local(utf8Cap.QuaternaryLocalId);

            int curPosTmp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(curPosTmp),
                new MirRvalue.Use(new MirOperand.CopyOrMove(posPlace, MirType.Int32, span)),
                span));
            var curPosOp = new MirOperand.CopyOrMove(MirPlace.Local(curPosTmp), MirType.Int32, span);

            int packedTmp = CreateTempLocal(MirType.Int64, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.Utf8ScanRaw,
                MirPlace.Local(packedTmp),
                null,
                false,
                new MirOperand[]
                {
                    srcOp,
                    curPosOp,
                    new MirOperand.Const(new MirConstant.Int32Const(utf8Cap.ScanMode), MirType.Int32, span),
                },
                Array.Empty<MirPlace>(),
                MirType.Int64,
                span));

            var packedOp = new MirOperand.CopyOrMove(MirPlace.Local(packedTmp), MirType.Int64, span);
            int endTmp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(endTmp),
                new MirRvalue.Cast(packedOp, MirType.Int32, false),
                span));

            int shiftedTmp = CreateTempLocal(MirType.Int64, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(shiftedTmp),
                new MirRvalue.Binary(
                    MirBinaryOp.UShr,
                    packedOp,
                    new MirOperand.Const(new MirConstant.Int32Const(32), MirType.Int32, span),
                    MirType.Int64,
                    false),
                span));

            _currentStmts.Add(new MirStatement.Assign(startPlace, new MirRvalue.Use(curPosOp), span));
            _currentStmts.Add(new MirStatement.Assign(
                lenPlace,
                new MirRvalue.Binary(
                    MirBinaryOp.Sub,
                    new MirOperand.CopyOrMove(MirPlace.Local(endTmp), MirType.Int32, span),
                    curPosOp,
                    MirType.Int32,
                    false),
                span));
            _currentStmts.Add(new MirStatement.Assign(
                posPlace,
                new MirRvalue.Cast(new MirOperand.CopyOrMove(MirPlace.Local(shiftedTmp), MirType.Int64, span), MirType.Int32, false),
                span));

            var target = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Bool, span));
            _currentStmts.Add(new MirStatement.Assign(
                target,
                new MirRvalue.Binary(
                    MirBinaryOp.Ge,
                    packedOp,
                    new MirOperand.Const(new MirConstant.Int64Const(0L), MirType.Int64, span),
                    MirType.Bool,
                    false),
                span));
            return new MirOperand.CopyOrMove(target, MirType.Bool, span);
        }

        if (method.Name == "MoveNext" &&
            inv.Instance is IFlowCaptureReferenceOperation moveNextFcr &&
            _captures.TryGetValue(moveNextFcr.Id, out var enumCap) &&
            enumCap.Kind == CaptureKind.SpanEnumerator)
        {
            var idxPlace = MirPlace.Local(enumCap.SecondaryLocalId);
            var spanPlace = MirPlace.Local(enumCap.PrimaryLocalId);
            var one = new MirOperand.Const(new MirConstant.Int32Const(1), MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                idxPlace,
                new MirRvalue.Binary(MirBinaryOp.Add, new MirOperand.CopyOrMove(idxPlace, MirType.Int32, span), one, MirType.Int32, false),
                span));

            int lenTemp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.SpanLength,
                MirPlace.Local(lenTemp),
                null,
                false,
                Array.Empty<MirOperand>(),
                new[] { spanPlace },
                MirType.Int32,
                span));

            var target = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Bool, span));
            _currentStmts.Add(new MirStatement.Assign(
                target,
                new MirRvalue.Binary(
                    MirBinaryOp.Lt,
                    new MirOperand.CopyOrMove(idxPlace, MirType.Int32, span),
                    new MirOperand.CopyOrMove(MirPlace.Local(lenTemp), MirType.Int32, span),
                    MirType.Bool,
                    false),
                span));
            return new MirOperand.CopyOrMove(target, MirType.Bool, span);
        }

        if ((ownerNs == "Vibranium" && owner?.Name is "Io" or "Prelude" && method.Name is "Write" or "WriteLine" or "Line") ||
            (ownerNs == "System" && owner?.Name == "Console" && method.Name is "Write" or "WriteLine"))
        {
            EmitWriteOrWriteLineIntrinsic(inv, method, owner!, span);
            return null;
        }

        if (ownerNs == "Vibranium" && owner is not null)
        {
            if (TryLowerVibraniumCoreInvocation(inv, method, owner, destPlace, span, out var intrResult))
            {
                return intrResult;
            }
        }

        if (owner is not null && TryLowerBclOrSpanExtensionInvocation(inv, method, owner, ownerNs, destPlace, span, out var bclResult))
        {
            return bclResult;
        }

        if (VibraniumSymbolHelpers.IsSpanType(owner, out var spanElem, out bool spanRo) &&
            method.Name == "Slice" &&
            inv.Instance is not null)
        {
            var resSpanType = new MirType.SpanType(MapMirType(SubstituteType(spanElem)), spanRo);
            var spanPlace = LowerPlaceOrMaterializeTemp(inv.Instance, resSpanType, isMutableAccess: false, span);
            spanPlace = ReserveReceiverPlaceBeforeArgs(spanPlace, resSpanType, isMutable: !spanRo, isTwoPhaseBorrow: !spanRo, span);
            var startOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);

            MirOperand lenOp;
            if (inv.Arguments.Length == 2)
            {
                lenOp = LowerOperand(inv.Arguments[1].Value, MirType.Int32);
            }
            else
            {
                int totalTemp = CreateTempLocal(MirType.Int32, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanLength,
                    MirPlace.Local(totalTemp),
                    null,
                    false,
                    Array.Empty<MirOperand>(),
                    new[] { spanPlace },
                    MirType.Int32,
                    span));
                int calcLenTemp = CreateTempLocal(MirType.Int32, span);
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(calcLenTemp),
                    new MirRvalue.Binary(MirBinaryOp.Sub, new MirOperand.CopyOrMove(MirPlace.Local(totalTemp), MirType.Int32, span), startOp, MirType.Int32, false),
                    span));
                lenOp = new MirOperand.CopyOrMove(MirPlace.Local(calcLenTemp), MirType.Int32, span);
            }

            var target = destPlace ?? MirPlace.Local(CreateTempLocal(resSpanType, span));
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.SpanSlice,
                target,
                null,
                false,
                new[] { startOp, lenOp },
                new[] { spanPlace },
                resSpanType,
                span));
            return new MirOperand.CopyOrMove(target, resSpanType, span);
        }

        if (!method.IsStatic &&
            method.Name == "Dispose" &&
            method.Parameters.Length == 0 &&
            (VibraniumSymbolHelpers.ImplementsIDisposable(method.ContainingType) ||
             method.ContainingType?.SpecialType == SpecialType.System_IDisposable ||
             (inv.Instance is not null && VibraniumSymbolHelpers.ImplementsIDisposable(SubstituteType(inv.Instance.Type)))))
        {
            EmitDiag(
                VibraniumDiagnosticIds.LinearResourceViolation,
                "Manual '.Dispose()' calls are banned in Vibranium; dispose linear IDisposable resources deterministically via 'using var' or move them by value.",
                span);
            return null;
        }

        if (owner?.SpecialType == SpecialType.System_Object && method.MethodKind == MethodKind.Constructor)
        {
            return null;
        }

        if (owner is not null && !IsInCompilationOrVibraniumStd(owner.ContainingAssembly))
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"External call '{owner.Name}.{method.Name}' is not supported in Vibranium.", span);
            return null;
        }

        if (method.IsExtern ||
            owner?.TypeKind == TypeKind.Interface ||
            VibraniumSymbolHelpers.HasInteropImportAttribute(method) ||
            (method.MethodKind is MethodKind.UserDefinedOperator or MethodKind.Conversion &&
             !VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(method)))
        {
            var stubRet = MapMirType(SubstituteType(method.ReturnType));
            if (stubRet is MirType.VoidType) return null;
            var stubDest = destPlace ?? MirPlace.Local(CreateTempLocal(stubRet, span));
            _currentStmts.Add(new MirStatement.Assign(stubDest, new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(stubRet), stubRet, span)), span));
            return new MirOperand.CopyOrMove(stubDest, stubRet, span);
        }

        _enqueueMethodInstantiation(method);
        if (owner is not null) _enqueueTypeInstantiation(owner);

        var callArgs = new List<MirCallArg>();
        MirPassKind? recvPass = null;
        if (method.IsStatic && inv.Instance is not null && method.Parameters.Length > 0)
        {
            callArgs.AddRange(OrderCallArgumentsWithReducedReceiver(inv.Instance, inv.Arguments, method.Parameters, span));
        }
        else if (!method.IsStatic && inv.Instance is not null)
        {
            recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(method);
            var recvMirType = MapMirType(SubstituteType(inv.Instance.Type));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(inv.Instance.Syntax);
            bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
            var wrapperRead = ReadsReceiverInPlace(method, inv.Instance, inv.Arguments.Length) ? BeginWrapperRead(inv.Instance) : null;
            var recvPlace = LowerPlaceOrMaterializeTemp(
                inv.Instance,
                recvMirType,
                isMutableAccess: isMutRecv,
                recvSpan,
                isTwoPhaseBorrow: isMutRecv && inv.Arguments.Length > 0);
            EndWrapperRead(wrapperRead);
            if (inv.Arguments.Length > 0)
            {
                recvPlace = ReserveReceiverPlaceBeforeArgs(
                    recvPlace,
                    recvMirType,
                    isMutable: isMutRecv,
                    isTwoPhaseBorrow: isMutRecv,
                    recvSpan);
            }
            callArgs.Add(WithCallHold(new MirCallArg(recvPass.Value, null, recvPlace, recvSpan), wrapperRead));
        }

        if (!(method.IsStatic && inv.Instance is not null && method.Parameters.Length > 0))
        {
            var explicitCallArgs = OrderCallArguments(inv.Arguments, method.Parameters, span, materializeByValueTemps: recvPass == MirPassKind.ImplicitThisMut);
            callArgs.AddRange(explicitCallArgs);
        }

        string calleeKey = VibraniumSymbolHelpers.GetMethodKey(method);

        if (method.ReturnsByRef || method.ReturnsByRefReadonly)
        {
            bool refMut = method.ReturnsByRef && !method.ReturnsByRefReadonly;
            var targetValType = MapMirType(SubstituteType(method.ReturnType));
            int refTempId = CreateTempRefLocal(targetValType, refMut, span);
            _currentStmts.Add(new MirStatement.Call(null, refTempId, refMut, calleeKey, callArgs, span));
            var derefPlace = new MirPlace(refTempId, new MirProjection[] { new MirProjection.Deref(targetValType) });
            if (destPlace is not null)
            {
                var op = new MirOperand.CopyOrMove(derefPlace, targetValType, span);
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(op), span));
                return new MirOperand.CopyOrMove(destPlace, targetValType, span);
            }
            return new MirOperand.CopyOrMove(derefPlace, targetValType, span);
        }

        var retType = MapMirType(SubstituteType(method.ReturnType));
        if (retType is MirType.VoidType)
        {
            _currentStmts.Add(new MirStatement.Call(null, null, false, calleeKey, callArgs, span));
            return null;
        }

        var finalDest = destPlace ?? MirPlace.Local(CreateTempLocal(retType, span));
        _currentStmts.Add(new MirStatement.Call(finalDest, null, false, calleeKey, callArgs, span));
        return new MirOperand.CopyOrMove(finalDest, retType, span);
    }

    private void EmitWriteOrWriteLineIntrinsic(
        IInvocationOperation inv,
        IMethodSymbol method,
        INamedTypeSymbol owner,
        VibraniumSpan span)
    {
        if (method.Name == "WriteLine" && inv.Arguments.Length == 0)
        {
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.IoWriteLineEmpty,
                null,
                null,
                false,
                Array.Empty<MirOperand>(),
                Array.Empty<MirPlace>(),
                MirType.Void,
                span));
            return;
        }

        if (inv.Arguments.Length != 1 || method.Name is not ("Write" or "WriteLine"))
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Overload '{owner.Name}.{method.Name}' with {inv.Arguments.Length} argument(s) is not supported in Vibranium.", span);
            return;
        }

        IOperation rawArg = inv.Arguments[0].Value;
        // System.Console.Write/WriteLine has no direct nint/nuint overload in BCL and binds to object?; unwrap nint/nuint boxing.
        if (method.Parameters.Length == 1 &&
            method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
            rawArg is IConversionOperation boxConv &&
            boxConv.Operand.Type?.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr)
        {
            rawArg = boxConv.Operand;
        }

        var argTypeSym = SubstituteType(rawArg.Type);
        if (argTypeSym is null)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Unsupported null argument in '{owner.Name}.{method.Name}'.", span);
            return;
        }

        bool isLine = method.Name == "WriteLine";
        switch (argTypeSym.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_IntPtr:
            {
                var argMirType = MapMirType(argTypeSym);
                var argOp = LowerOperand(rawArg, argMirType);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    isLine ? MirIntrinsicKind.IoWriteLineInt64 : MirIntrinsicKind.IoWriteInt64,
                    null,
                    null,
                    false,
                    new[] { argOp },
                    Array.Empty<MirPlace>(),
                    MirType.Void,
                    span));
                return;
            }

            case SpecialType.System_Byte:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
            case SpecialType.System_UIntPtr:
            {
                var argMirType = MapMirType(argTypeSym);
                var argOp = LowerOperand(rawArg, argMirType);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    isLine ? MirIntrinsicKind.IoWriteLineUInt64 : MirIntrinsicKind.IoWriteUInt64,
                    null,
                    null,
                    false,
                    new[] { argOp },
                    Array.Empty<MirPlace>(),
                    MirType.Void,
                    span));
                return;
            }

            case SpecialType.System_Boolean:
            {
                var argOp = LowerOperand(rawArg, MirType.Bool);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    isLine ? MirIntrinsicKind.IoWriteLineBool : MirIntrinsicKind.IoWriteBool,
                    null,
                    null,
                    false,
                    new[] { argOp },
                    Array.Empty<MirPlace>(),
                    MirType.Void,
                    span));
                return;
            }

            case SpecialType.System_String:
            {
                var argOp = LowerOperand(rawArg, MirType.StringLiteral);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    isLine ? MirIntrinsicKind.IoWriteLineStr : MirIntrinsicKind.IoWriteStr,
                    null,
                    null,
                    false,
                    new[] { argOp },
                    Array.Empty<MirPlace>(),
                    MirType.Void,
                    span));
                return;
            }

            default:
                if (owner.ContainingNamespace?.ToDisplayString() == "Vibranium" &&
                    owner.Name is "Io" or "Prelude" &&
                    VibraniumSymbolHelpers.IsSpanType(argTypeSym, out var spanElem, out _) &&
                    spanElem.SpecialType == SpecialType.System_Byte)
                {
                    var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                    var bytesOp = LowerOperand(rawArg, u8SpanType);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.StdioWriteOut,
                        null,
                        null,
                        false,
                        new[] { bytesOp },
                        Array.Empty<MirPlace>(),
                        MirType.Void,
                        span));
                    if (isLine)
                    {
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.IoWriteLineEmpty,
                            null,
                            null,
                            false,
                            Array.Empty<MirOperand>(),
                            Array.Empty<MirPlace>(),
                            MirType.Void,
                            span));
                    }
                    return;
                }

                EmitDiag(
                    VibraniumDiagnosticIds.Unsupported,
                    $"Overload '{owner.Name}.{method.Name}({argTypeSym.ToDisplayString()})' is not supported in Vibranium.",
                    span);
                return;
        }
    }

    private bool TryLowerVibraniumCoreInvocation(
        IInvocationOperation inv,
        IMethodSymbol method,
        INamedTypeSymbol owner,
        MirPlace? destPlace,
        VibraniumSpan span,
        out MirOperand? result)
    {
        result = null;

        switch (owner.Name)
        {
            case "Prelude":
            {
                switch (method.Name)
                {
                    case "Some":
                    {
                        var innerT = MapMirType(SubstituteType(method.TypeArguments[0]));
                        var wrapT = new MirType.SomeWrapperType(innerT);
                        var valOp = LowerOperand(inv.Arguments[0].Value, innerT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(wrapT, span));
                        _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.MakeSomeWrapper(innerT, valOp), span));
                        result = new MirOperand.CopyOrMove(dest, wrapT, span);
                        return true;
                    }
                    case "Ok":
                    {
                        if (method.TypeArguments.Length == 0 && inv.Arguments.Length == 0)
                        {
                            var unitWrapT = new MirType.OkWrapperType(MirType.NoneUnit);
                            var unitValOp = new MirOperand.Const(new MirConstant.ZeroDefault(MirType.NoneUnit), MirType.NoneUnit, span);
                            var unitDest = destPlace ?? MirPlace.Local(CreateTempLocal(unitWrapT, span));
                            _currentStmts.Add(new MirStatement.Assign(unitDest, new MirRvalue.MakeOkWrapper(MirType.NoneUnit, unitValOp), span));
                            result = new MirOperand.CopyOrMove(unitDest, unitWrapT, span);
                            return true;
                        }
                        var okT = MapMirType(SubstituteType(method.TypeArguments[0]));
                        var wrapT = new MirType.OkWrapperType(okT);
                        var valOp = LowerOperand(inv.Arguments[0].Value, okT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(wrapT, span));
                        _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.MakeOkWrapper(okT, valOp), span));
                        result = new MirOperand.CopyOrMove(dest, wrapT, span);
                        return true;
                    }
                    case "Err":
                    {
                        var errT = MapMirType(SubstituteType(method.TypeArguments[0]));
                        var wrapT = new MirType.ErrWrapperType(errT);
                        var valOp = LowerOperand(inv.Arguments[0].Value, errT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(wrapT, span));
                        _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.MakeErrWrapper(errT, valOp), span));
                        result = new MirOperand.CopyOrMove(dest, wrapT, span);
                        return true;
                    }
                    case "Move":
                    {
                        var t = MapMirType(SubstituteType(method.TypeArguments[0]));
                        var valOp = LowerOperand(inv.Arguments[0].Value, t);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(t, span));
                        _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.Use(valOp), span));
                        result = new MirOperand.CopyOrMove(dest, t, span);
                        return true;
                    }
                }
                break;
            }

            case "Io":
                break;

            case "Stdio" when method.Name is "WriteOut" or "WriteErr" && inv.Arguments.Length == 1:
            {
                var argVal = inv.Arguments[0].Value;
                var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                var bytesOp = SubstituteType(argVal.Type)?.SpecialType == SpecialType.System_String
                    ? LowerStringAsUtf8Operand(argVal, span)
                    : LowerOperand(argVal, u8SpanType);
                var intrKind = method.Name == "WriteOut" ? MirIntrinsicKind.StdioWriteOut : MirIntrinsicKind.StdioWriteErr;
                _currentStmts.Add(new MirStatement.Intrinsic(
                    intrKind, null, null, false, new[] { bytesOp }, Array.Empty<MirPlace>(), MirType.Void, span));
                return true;
            }

            case "StringUtf8Extensions" when method.Name == "AsUtf8":
            {
                IOperation strExpr = inv.Instance ?? inv.Arguments[0].Value;
                var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                var dest = destPlace ?? MirPlace.Local(CreateTempLocal(u8SpanType, span));
                if (strExpr.ConstantValue is { HasValue: true, Value: string constStr })
                {
                    byte[] utf8Bytes = Encoding.UTF8.GetBytes(constStr);
                    _currentStmts.Add(new MirStatement.Assign(
                        dest,
                        new MirRvalue.Use(new MirOperand.Const(new MirConstant.Utf8BytesConst(utf8Bytes), u8SpanType, span)),
                        span));
                }
                else
                {
                    var strOp = LowerOperand(strExpr, MirType.StringLiteral);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.StringAsUtf8,
                        dest,
                        null,
                        false,
                        new[] { strOp },
                        Array.Empty<MirPlace>(),
                        u8SpanType,
                        span));
                }
                result = new MirOperand.CopyOrMove(dest, u8SpanType, span);
                return true;
            }

            case "StringUtf8Extensions" when method.Name == "Lines":
            {
                IOperation strExpr = inv.Instance ?? inv.Arguments[0].Value;
                var roSpanOp = LowerStringAsUtf8Operand(strExpr, span);
                var dest = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Utf8Lines, span));
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanToUtf8View,
                    dest,
                    null,
                    false,
                    new[] { roSpanOp },
                    Array.Empty<MirPlace>(),
                    MirType.Utf8Lines,
                    span));
                result = new MirOperand.CopyOrMove(dest, MirType.Utf8Lines, span);
                return true;
            }

            case "Utf8SpanExtensions" when method.Name == "Lines":
            {
                IOperation spanExpr = inv.Instance ?? inv.Arguments[0].Value;
                var roSpanT = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                MirOperand roSpanOp;
                if (VibraniumSymbolHelpers.IsSpanType(SubstituteType(spanExpr.Type), out _, out bool srcIsRo) && !srcIsRo)
                {
                    var mutSpanT = new MirType.SpanType(MirType.UInt8, IsReadOnly: false);
                    var spanPlace = LowerPlaceOrMaterializeTemp(spanExpr, mutSpanT, isMutableAccess: false, span);
                    int roTmp = CreateTempLocal(roSpanT, span);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.SpanToReadOnly,
                        MirPlace.Local(roTmp),
                        null,
                        false,
                        Array.Empty<MirOperand>(),
                        new[] { spanPlace },
                        roSpanT,
                        span));
                    roSpanOp = new MirOperand.CopyOrMove(MirPlace.Local(roTmp), roSpanT, span);
                }
                else
                {
                    roSpanOp = LowerOperand(spanExpr, roSpanT);
                }

                var dest = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Utf8Lines, span));
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanToUtf8View,
                    dest,
                    null,
                    false,
                    new[] { roSpanOp },
                    Array.Empty<MirPlace>(),
                    MirType.Utf8Lines,
                    span));
                result = new MirOperand.CopyOrMove(dest, MirType.Utf8Lines, span);
                return true;
            }

            case "Utf8Lines" when method.Name == "GetEnumerator" && inv.Instance is not null:
            {
                var viewOp = LowerOperand(inv.Instance, MirType.Utf8Lines);
                var dest = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Utf8Lines, span));
                _currentStmts.Add(new MirStatement.Assign(dest, new MirRvalue.Use(viewOp), span));
                result = new MirOperand.CopyOrMove(dest, MirType.Utf8Lines, span);
                return true;
            }

            case "VibraniumRuntime" when method.Name == "Panic" && inv.Arguments.Length == 1:
            {
                var msgOp = LowerOperand(inv.Arguments[0].Value, MirType.StringLiteral);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.Panic, null, null, false, new[] { msgOp }, Array.Empty<MirPlace>(), MirType.Void, span));
                return true;
            }

            case "OptionExtensions" when VibraniumSymbolHelpers.IsCoreOptionExtensions(owner):
            {
                IOperation slotExpr = inv.Instance ?? inv.Arguments[0].Value;
                int extraArgOffset = inv.Instance is null ? 1 : 0;
                var innerT = MapMirType(SubstituteType(method.TypeArguments[0]));
                if (_module.IsLinearResource(innerT))
                {
                    EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, $"'Option.{method.Name}' cannot be used on linear resource '{innerT}'.", span);
                }

                switch (method.Name)
                {
                    case "AsRef":
                    {
                        var resT = new MirType.OptionRefType(innerT);
                        var wrapperRead = BeginWrapperRead(slotExpr);
                        var slotPlace = LowerPlaceOrMaterializeTemp(slotExpr, new MirType.OptionType(innerT), isMutableAccess: false, span);
                        EndWrapperRead(wrapperRead);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionAsRef, dest, null, false, Array.Empty<MirOperand>(), new[] { slotPlace }, resT, span)
                        {
                            Hold = wrapperRead?.Hold,
                            ReleasesMovedPlace = wrapperRead?.Held,
                        });
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "AsMut":
                    {
                        var resT = new MirType.OptionMutType(innerT);
                        var slotPlace = LowerPlace(slotExpr, isMutableAccess: true);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionAsMut, dest, null, false, Array.Empty<MirOperand>(), new[] { slotPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "Take":
                    {
                        var resT = new MirType.OptionType(innerT);
                        var slotPlace = LowerPlace(slotExpr, isMutableAccess: true);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionTake, dest, null, false, Array.Empty<MirOperand>(), new[] { slotPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "Replace":
                    {
                        var resT = new MirType.OptionType(innerT);
                        var slotPlace = LowerPlace(slotExpr, isMutableAccess: true, isTwoPhaseBorrow: true);
                        slotPlace = ReserveReceiverPlaceBeforeArgs(slotPlace, resT, isMutable: true, isTwoPhaseBorrow: true, span);
                        var repOp = LowerOperand(inv.Arguments[extraArgOffset].Value, resT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionReplace, dest, null, false, new[] { repOp }, new[] { slotPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                }
                break;
            }

            case "Option" when inv.Instance is not null:
            {
                var innerT = MapMirType(SubstituteType(owner.TypeArguments[0]));
                var optT = new MirType.OptionType(innerT);

                switch (method.Name)
                {
                    case "Unwrap":
                    {
                        var optOp = LowerOperand(inv.Instance, optT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(innerT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionUnwrap, dest, null, false, new[] { optOp }, Array.Empty<MirPlace>(), innerT, span));
                        result = new MirOperand.CopyOrMove(dest, innerT, span);
                        return true;
                    }
                    case "Expect":
                    {
                        var optRead = ReserveReceiverOperandBeforeArgs(inv.Instance, optT, span);
                        var msgOp = LowerOperand(inv.Arguments[0].Value, MirType.StringLiteral);
                        var optOp = optRead();
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(innerT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionExpect, dest, null, false, new[] { optOp, msgOp }, Array.Empty<MirPlace>(), innerT, span));
                        result = new MirOperand.CopyOrMove(dest, innerT, span);
                        return true;
                    }
                    case "UnwrapOr":
                    {
                        if (_module.IsLinearResource(innerT))
                        {
                            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Option.UnwrapOr' cannot be used on linear IDisposable resources.", span);
                        }
                        var optRead = ReserveReceiverOperandBeforeArgs(inv.Instance, optT, span);
                        var fbOp = LowerOperand(inv.Arguments[0].Value, innerT);
                        var optOp = optRead();
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(innerT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionUnwrapOr, dest, null, false, new[] { optOp, fbOp }, Array.Empty<MirPlace>(), innerT, span));
                        result = new MirOperand.CopyOrMove(dest, innerT, span);
                        return true;
                    }
                    case "UnwrapOrDefault":
                    {
                        if (_module.IsLinearResource(optT))
                        {
                            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Option.UnwrapOrDefault' cannot be used on linear IDisposable resources.", span);
                        }
                        else if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(SubstituteType(owner.TypeArguments[0]), _compilation))
                        {
                            EmitDiag(VibraniumDiagnosticIds.NullOrZeroUnsafe, "'Option.UnwrapOrDefault' requires a zero-safe payload type.", span);
                        }
                        var optOp = LowerOperand(inv.Instance, optT);
                        var zeroOp = new MirOperand.Const(new MirConstant.ZeroDefault(innerT), innerT, span);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(innerT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionUnwrapOr, dest, null, false, new[] { optOp, zeroOp }, Array.Empty<MirPlace>(), innerT, span));
                        result = new MirOperand.CopyOrMove(dest, innerT, span);
                        return true;
                    }
                    case "UnwrapMut":
                    {
                        if (_module.IsLinearResource(innerT))
                        {
                            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Option.UnwrapMut' cannot be used on linear IDisposable resources.", span);
                        }
                        var slotPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        int refId = CreateTempRefLocal(innerT, isMutable: true, span);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionUnwrapMut, null, refId, true, Array.Empty<MirOperand>(), new[] { slotPlace }, new MirType.RefType(innerT, true), span));
                        var derefPlace = new MirPlace(refId, new MirProjection[] { new MirProjection.Deref(innerT) });
                        if (destPlace is not null)
                        {
                            var op = new MirOperand.CopyOrMove(derefPlace, innerT, span);
                            _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(op), span));
                            result = new MirOperand.CopyOrMove(destPlace, innerT, span);
                        }
                        else
                        {
                            result = new MirOperand.CopyOrMove(derefPlace, innerT, span);
                        }
                        return true;
                    }
                    case "IsNone" when inv.Arguments.Length == 1:
                    {
                        EmitDiag(VibraniumDiagnosticIds.Unsupported, "'Option.IsNone(out ...)' must be used directly as an 'if' guard condition.", span);
                        result = EmitInvalidGuardFallbackResult(inv.Arguments, destPlace, span);
                        return true;
                    }
                }
                break;
            }

            case "OptionMut" when inv.Instance is not null:
            {
                var innerT = MapMirType(SubstituteType(owner.TypeArguments[0]));
                var optT = new MirType.OptionType(innerT);
                if (_module.IsLinearResource(innerT))
                {
                    EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, $"'OptionMut.{method.Name}' cannot be used on linear IDisposable resources.", span);
                }

                switch (method.Name)
                {
                    case "Set":
                    {
                        var curPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        var valOp = LowerOperand(inv.Arguments[0].Value, optT);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionMutSet, null, null, false, new[] { valOp }, new[] { curPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Take":
                    {
                        var curPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(optT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionMutTake, dest, null, false, Array.Empty<MirOperand>(), new[] { curPlace }, optT, span));
                        result = new MirOperand.CopyOrMove(dest, optT, span);
                        return true;
                    }
                    case "Replace":
                    {
                        var curPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        var valOp = LowerOperand(inv.Arguments[0].Value, optT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(optT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionMutReplace, dest, null, false, new[] { valOp }, new[] { curPlace }, optT, span));
                        result = new MirOperand.CopyOrMove(dest, optT, span);
                        return true;
                    }
                    case "AsRef":
                    {
                        var resT = new MirType.OptionRefType(innerT);
                        var curPlace = LowerPlace(inv.Instance, isMutableAccess: false);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.OptionMutAsRef, dest, null, false, Array.Empty<MirOperand>(), new[] { curPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                }
                break;
            }

            case "Result" when inv.Instance is not null:
            {
                var okT = MapMirType(SubstituteType(owner.TypeArguments[0]));
                var errT = MapMirType(SubstituteType(owner.TypeArguments[1]));
                var resT = new MirType.ResultType(okT, errT);

                switch (method.Name)
                {
                    case "Unwrap":
                    {
                        var resOp = LowerOperand(inv.Instance, resT);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(okT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.ResultUnwrap, dest, null, false, new[] { resOp }, Array.Empty<MirPlace>(), okT, span));
                        result = new MirOperand.CopyOrMove(dest, okT, span);
                        return true;
                    }
                    case "Expect":
                    {
                        var resRead = ReserveReceiverOperandBeforeArgs(inv.Instance, resT, span);
                        var msgOp = LowerOperand(inv.Arguments[0].Value, MirType.StringLiteral);
                        var resOp = resRead();
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(okT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.ResultExpect, dest, null, false, new[] { resOp, msgOp }, Array.Empty<MirPlace>(), okT, span));
                        result = new MirOperand.CopyOrMove(dest, okT, span);
                        return true;
                    }
                    case "UnwrapOr":
                    {
                        if (_module.IsLinearResource(resT))
                        {
                            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Result.UnwrapOr' cannot be used on linear IDisposable resources.", span);
                        }
                        var resRead = ReserveReceiverOperandBeforeArgs(inv.Instance, resT, span);
                        var fbOp = LowerOperand(inv.Arguments[0].Value, okT);
                        var resOp = resRead();
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(okT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.ResultUnwrapOr, dest, null, false, new[] { resOp, fbOp }, Array.Empty<MirPlace>(), okT, span));
                        result = new MirOperand.CopyOrMove(dest, okT, span);
                        return true;
                    }
                    case "UnwrapOrDefault":
                    {
                        if (_module.IsLinearResource(resT))
                        {
                            EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Result.UnwrapOrDefault' cannot be used on linear IDisposable resources.", span);
                        }
                        else if (!VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(SubstituteType(owner.TypeArguments[0]), _compilation))
                        {
                            EmitDiag(VibraniumDiagnosticIds.NullOrZeroUnsafe, "'Result.UnwrapOrDefault' requires a zero-safe Ok payload type.", span);
                        }
                        var resOp = LowerOperand(inv.Instance, resT);
                        var zeroOp = new MirOperand.Const(new MirConstant.ZeroDefault(okT), okT, span);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(okT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.ResultUnwrapOr, dest, null, false, new[] { resOp, zeroOp }, Array.Empty<MirPlace>(), okT, span));
                        result = new MirOperand.CopyOrMove(dest, okT, span);
                        return true;
                    }
                    case "IsErr" when inv.Arguments.Length == 2:
                    {
                        EmitDiag(VibraniumDiagnosticIds.Unsupported, "'Result.IsErr(out ..., out ...)' must be used directly as an 'if' guard condition.", span);
                        result = EmitInvalidGuardFallbackResult(inv.Arguments, destPlace, span);
                        return true;
                    }
                }
                break;
            }

            case "Vec" when inv.Instance is not null:
            {
                var elemT = MapMirType(SubstituteType(owner.TypeArguments[0]));

                switch (method.Name)
                {
                    case "Push":
                    case "Add":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var itemOp = LowerOperand(inv.Arguments[0].Value, elemT);
                        if (itemOp is MirOperand.CopyOrMove)
                        {
                            int itemTemp = CreateTempLocal(elemT, span);
                            _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(itemTemp), new MirRvalue.Use(itemOp), span));
                            itemOp = new MirOperand.CopyOrMove(MirPlace.Local(itemTemp), elemT, span);
                        }
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecPush, null, null, false, new[] { itemOp }, new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Pop":
                    {
                        var resT = new MirType.OptionType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecPop, dest, null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "Clear":
                    {
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecClear, null, null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Truncate":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var lenOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecTruncate, null, null, false, new[] { lenOp }, new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Reserve":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var addOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecReserve, null, null, false, new[] { addOp }, new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Extend":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var roSpanType = new MirType.SpanType(elemT, IsReadOnly: true);
                        var itemsOp = LowerOperand(inv.Arguments[0].Value, roSpanType);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecExtend, null, null, false, new[] { itemsOp }, new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Resize":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var lenOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);
                        var fillOp = LowerOperand(inv.Arguments[1].Value, elemT);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecResize, null, null, false, new[] { lenOp, fillOp }, new[] { vecPlace }, MirType.Void, span));
                        return true;
                    }
                    case "Slice":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var resT = new MirType.SpanType(elemT, false);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true, isTwoPhaseBorrow: true);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: true, isTwoPhaseBorrow: true, span);
                        var startOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);
                        var lenOp = LowerOperand(inv.Arguments[1].Value, MirType.Int32);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecSliceMut, dest, null, false, new[] { startOp, lenOp }, new[] { vecPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "ReadOnlySlice":
                    {
                        var vecMirType = new MirType.VecType(elemT);
                        var resT = new MirType.SpanType(elemT, true);
                        var wrapperRead = BeginWrapperRead(inv.Instance);
                        var vecPlace = LowerPlaceOrMaterializeTemp(inv.Instance, vecMirType, isMutableAccess: false, span);
                        EndWrapperRead(wrapperRead);
                        vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirType, isMutable: false, isTwoPhaseBorrow: false, span);
                        var startOp = LowerOperand(inv.Arguments[0].Value, MirType.Int32);
                        var lenOp = LowerOperand(inv.Arguments[1].Value, MirType.Int32);
                        int roTmp = CreateTempLocal(resT, span);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecAsReadOnlySpan, MirPlace.Local(roTmp), null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, resT, span)
                        {
                            Hold = wrapperRead?.Hold,
                            ReleasesMovedPlace = wrapperRead?.Held,
                        });
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.SpanSlice, dest, null, false, new[] { startOp, lenOp }, new[] { MirPlace.Local(roTmp) }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "AsSpan":
                    {
                        var resT = new MirType.SpanType(elemT, false);
                        var vecPlace = LowerPlace(inv.Instance, isMutableAccess: true);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecAsSpan, dest, null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                    case "AsReadOnlySpan":
                    {
                        var resT = new MirType.SpanType(elemT, true);
                        var vecPlace = LowerPlaceOrMaterializeTemp(inv.Instance, new MirType.VecType(elemT), isMutableAccess: false, span);
                        var dest = destPlace ?? MirPlace.Local(CreateTempLocal(resT, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.VecAsReadOnlySpan, dest, null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, resT, span));
                        result = new MirOperand.CopyOrMove(dest, resT, span);
                        return true;
                    }
                }
                break;
            }
        }

        return false;
    }

    private MirOperand LowerStringAsUtf8Operand(IOperation strExpr, VibraniumSpan span)
    {
        var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
        if (strExpr.ConstantValue is { HasValue: true, Value: string constStr })
        {
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(constStr);
            return new MirOperand.Const(new MirConstant.Utf8BytesConst(utf8Bytes), u8SpanType, span);
        }

        var strOp = LowerOperand(strExpr, MirType.StringLiteral);
        int tmp = CreateTempLocal(u8SpanType, span);
        _currentStmts.Add(new MirStatement.Intrinsic(
            MirIntrinsicKind.StringAsUtf8,
            MirPlace.Local(tmp),
            null,
            false,
            new[] { strOp },
            Array.Empty<MirPlace>(),
            u8SpanType,
            span));
        return new MirOperand.CopyOrMove(MirPlace.Local(tmp), u8SpanType, span);
    }

    private bool TryLowerBclOrSpanExtensionInvocation(
        IInvocationOperation inv,
        IMethodSymbol method,
        INamedTypeSymbol owner,
        string ownerNs,
        MirPlace? destPlace,
        VibraniumSpan span,
        out MirOperand? result)
    {
        result = null;

        // 1. int.TryParse / long.TryParse -> Vibranium.Std.Bytes.TryParseInt32 / TryParseInt64
        if (owner.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64 &&
            method.Name == "TryParse" &&
            !method.IsGenericMethod &&
            method.Parameters.Length == 2 &&
            inv.Arguments.Length == 2 &&
            method.Parameters[1].RefKind == RefKind.Out &&
            method.Parameters[1].Type.SpecialType == owner.SpecialType)
        {
            var p0Type = method.Parameters[0].Type;
            bool isStringArg = p0Type.SpecialType == SpecialType.System_String;
            bool isUtf8SpanArg = VibraniumSymbolHelpers.IsSpanType(p0Type, out var p0Elem, out bool p0Ro) &&
                p0Elem.SpecialType == SpecialType.System_Byte && p0Ro;
            if (!isStringArg && !isUtf8SpanArg)
            {
                return false;
            }

            var bytesType = _compilation.GetTypeByMetadataName("Vibranium.Std.Bytes");
            if (bytesType is null)
            {
                return false;
            }

            string targetName = owner.SpecialType == SpecialType.System_Int32 ? "TryParseInt32" : "TryParseInt64";
            var stdMethod = bytesType.GetMembers(targetName).OfType<IMethodSymbol>().FirstOrDefault(
                static m => m.Parameters.Length == 2 &&
                            VibraniumSymbolHelpers.IsSpanType(m.Parameters[0].Type, out var e, out bool ro) &&
                            e.SpecialType == SpecialType.System_Byte &&
                            ro);
            if (stdMethod is null)
            {
                return false;
            }

            _enqueueTypeInstantiation(bytesType);
            _enqueueMethodInstantiation(stdMethod);

            IArgumentOperation? arg0 = null;
            IArgumentOperation? arg1 = null;
            foreach (var arg in inv.Arguments)
            {
                if (arg.Parameter?.Ordinal == 0) arg0 = arg;
                else if (arg.Parameter?.Ordinal == 1) arg1 = arg;
            }
            if (arg0 is null || arg1 is null)
            {
                return false;
            }

            var u8SpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
            var outMirType = owner.SpecialType == SpecialType.System_Int32 ? MirType.Int32 : MirType.Int64;
            var arg0Span = VibraniumSymbolHelpers.ToVibraniumSpan(arg0.Syntax);
            var arg1Span = VibraniumSymbolHelpers.ToVibraniumSpan(arg1.Syntax);

            MirOperand LowerParseInput() => isStringArg
                ? LowerStringAsUtf8Operand(arg0.Value, arg0Span)
                : LowerOperand(arg0.Value, u8SpanType);

            MirPlace LowerOutTarget()
            {
                if (arg1.Value is IDeclarationExpressionOperation { Expression: ILocalReferenceOperation outLr })
                {
                    return MirPlace.Local(GetOrCreateLocalId(outLr.Local, isPatternBindingOverride: null));
                }
                if (arg1.Value is IDiscardOperation)
                {
                    return MirPlace.Local(CreateTempLocal(outMirType, arg1Span, "__out_discard"));
                }
                if (UnwrapImplicitIdentityConversion(arg1.Value) is IParameterReferenceOperation argPr &&
                    IsCapturedPrimaryConstructorParameter(argPr.Parameter))
                {
                    EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot pass captured primary-constructor parameter '{argPr.Parameter.Name}' by 'out'.", arg1Span);
                }
                return LowerPlace(arg1.Value, isMutableAccess: true, isTwoPhaseBorrow: true);
            }

            MirOperand utf8Op;
            MirPlace outPlace;
            if (arg0.Syntax.SpanStart <= arg1.Syntax.SpanStart)
            {
                utf8Op = LowerParseInput();
                outPlace = LowerOutTarget();
            }
            else
            {
                outPlace = LowerOutTarget();
                if (outPlace.Projections.Count > 0)
                {
                    outPlace = ReserveReceiverPlaceBeforeArgs(outPlace, outMirType, isMutable: true, isTwoPhaseBorrow: true, arg1Span, isOut: true);
                }
                utf8Op = LowerParseInput();
            }

            var callArgs = new List<MirCallArg>(2)
            {
                new(MirPassKind.ByValue, utf8Op, null, arg0Span),
                new(MirPassKind.Out, null, outPlace, arg1Span),
            };
            var finalDest = destPlace ?? MirPlace.Local(CreateTempLocal(MirType.Bool, span));
            _currentStmts.Add(new MirStatement.Call(finalDest, null, false, VibraniumSymbolHelpers.GetMethodKey(stdMethod), callArgs, span));
            result = new MirOperand.CopyOrMove(finalDest, MirType.Bool, span);
            return true;
        }

        // 2. System.MemoryExtensions.<byte> AND Vibranium.Std.ByteSpanExtensions(Span<byte>, ...) -> ReadOnlySpan<byte> receiver overload
        bool isBclMemExt = ownerNs == "System" &&
            owner.Name == "MemoryExtensions" &&
            method.IsGenericMethod &&
            method.TypeArguments.Length == 1 &&
            method.TypeArguments[0].SpecialType == SpecialType.System_Byte;
        bool isStdMutSpanExt = ownerNs == "Vibranium.Std" &&
            owner.Name == "ByteSpanExtensions" &&
            method.Parameters.Length == 2 &&
            VibraniumSymbolHelpers.IsSpanType(method.Parameters[0].Type, out var stdElem0, out bool stdRo0) &&
            stdElem0.SpecialType == SpecialType.System_Byte &&
            !stdRo0;

        if ((isBclMemExt || isStdMutSpanExt) &&
            method.Name is "SequenceEqual" or "StartsWith" or "EndsWith" or "IndexOf" or "LastIndexOf" or "Contains" &&
            method.Parameters.Length == 2)
        {
            var p0Type = method.Parameters[0].Type;
            var p1Type = method.Parameters[1].Type;
            if (!VibraniumSymbolHelpers.IsSpanType(p0Type, out var elem0, out bool p0IsRo) ||
                elem0.SpecialType != SpecialType.System_Byte)
            {
                return false;
            }

            bool p1IsByte = p1Type.SpecialType == SpecialType.System_Byte;
            bool p1IsString = p1Type.SpecialType == SpecialType.System_String;
            bool p1IsRoByteSpan = VibraniumSymbolHelpers.IsSpanType(p1Type, out var elem1, out bool p1Ro) &&
                elem1.SpecialType == SpecialType.System_Byte && p1Ro;
            if (!p1IsByte && !p1IsString && !p1IsRoByteSpan)
            {
                return false;
            }

            var stdExtType = _compilation.GetTypeByMetadataName("Vibranium.Std.ByteSpanExtensions");
            if (stdExtType is null)
            {
                return false;
            }

            IMethodSymbol? targetStdMethod = null;
            foreach (var cand in stdExtType.GetMembers(method.Name).OfType<IMethodSymbol>())
            {
                if (cand.Parameters.Length != 2) continue;
                if (!VibraniumSymbolHelpers.IsSpanType(cand.Parameters[0].Type, out var cElem0, out bool cRo0) ||
                    cElem0.SpecialType != SpecialType.System_Byte || !cRo0)
                {
                    continue;
                }

                var cp1 = cand.Parameters[1].Type;
                if ((p1IsByte && cp1.SpecialType == SpecialType.System_Byte) ||
                    (p1IsString && cp1.SpecialType == SpecialType.System_String) ||
                    (p1IsRoByteSpan && VibraniumSymbolHelpers.IsSpanType(cp1, out var cElem1, out bool cRo1) &&
                     cElem1.SpecialType == SpecialType.System_Byte && cRo1))
                {
                    targetStdMethod = cand;
                    break;
                }
            }

            if (targetStdMethod is null)
            {
                return false;
            }

            _enqueueTypeInstantiation(stdExtType);
            _enqueueMethodInstantiation(targetStdMethod);

            IOperation? expr0 = inv.Instance;
            IOperation? expr1 = null;
            bool reducedReceiverFirst = inv.Instance is not null;
            int pos0 = expr0?.Syntax.SpanStart ?? int.MaxValue;
            int pos1 = int.MaxValue;
            VibraniumSpan arg0Span = expr0 is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(expr0.Syntax) : span;
            VibraniumSpan arg1Span = span;

            foreach (var arg in inv.Arguments)
            {
                if (inv.Instance is null && arg.Parameter?.Ordinal == 0)
                {
                    expr0 = arg.Value;
                    if (arg.IsImplicit)
                    {
                        reducedReceiverFirst = true;
                    }
                    pos0 = arg.Value.Syntax.SpanStart;
                    arg0Span = VibraniumSymbolHelpers.ToVibraniumSpan(arg.Value.Syntax);
                }
                else if (arg.Parameter?.Ordinal == (inv.Instance is null ? 1 : 0) || arg.Parameter?.Ordinal == 1)
                {
                    expr1 = arg.Value;
                    pos1 = arg.Syntax.SpanStart;
                    arg1Span = VibraniumSymbolHelpers.ToVibraniumSpan(arg.Syntax);
                }
            }

            if (expr0 is null || expr1 is null)
            {
                return false;
            }

            var roSpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
            var mutSpanType = new MirType.SpanType(MirType.UInt8, IsReadOnly: false);
            var arg1MirType = MapMirType(SubstituteType(targetStdMethod.Parameters[1].Type));

            MirOperand LowerFirstSpan()
            {
                if (!p0IsRo)
                {
                    var spanPlace = LowerPlaceOrMaterializeTemp(expr0, mutSpanType, isMutableAccess: false, arg0Span);
                    int roTmp = CreateTempLocal(roSpanType, arg0Span);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.SpanToReadOnly,
                        MirPlace.Local(roTmp),
                        null,
                        false,
                        Array.Empty<MirOperand>(),
                        new[] { spanPlace },
                        roSpanType,
                        arg0Span));
                    return new MirOperand.CopyOrMove(MirPlace.Local(roTmp), roSpanType, arg0Span);
                }
                return LowerOperand(expr0, roSpanType);
            }

            MirOperand op0;
            MirOperand op1;
            if (reducedReceiverFirst || pos0 <= pos1)
            {
                op0 = LowerFirstSpan();
                op1 = LowerOperand(expr1, arg1MirType);
            }
            else
            {
                op1 = LowerOperand(expr1, arg1MirType);
                op0 = LowerFirstSpan();
            }

            var retMirType = MapMirType(SubstituteType(targetStdMethod.ReturnType));
            var finalDest = destPlace ?? MirPlace.Local(CreateTempLocal(retMirType, span));
            var callArgs = new List<MirCallArg>(2)
            {
                new(MirPassKind.ByValue, op0, null, arg0Span),
                new(MirPassKind.ByValue, op1, null, arg1Span),
            };
            _currentStmts.Add(new MirStatement.Call(finalDest, null, false, VibraniumSymbolHelpers.GetMethodKey(targetStdMethod), callArgs, span));
            result = new MirOperand.CopyOrMove(finalDest, retMirType, span);
            return true;
        }

        return false;
    }

    private bool TryEmitPropertyReferenceInto(
        IPropertyReferenceOperation prop,
        MirPlace destPlace,
        MirType targetMirType,
        VibraniumSpan span)
    {

        if (prop.Property.IsStatic && prop.Property.ContainingType.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr &&
            prop.Property.Name is "MinValue" or "MaxValue")
        {
            bool signed = prop.Property.ContainingType.SpecialType == SpecialType.System_IntPtr;
            long bits = prop.Property.Name == "MinValue" ? (signed ? long.MinValue : 0) : (signed ? long.MaxValue : -1);
            _currentStmts.Add(new MirStatement.Assign(destPlace,
                new MirRvalue.Use(new MirOperand.Const(new MirConstant.Int64Const(bits), targetMirType, span)), span));
            return true;
        }
        var owner = SubstituteType(prop.Property.ContainingType);

        if (prop.Property.Name == "Current" &&
            prop.Instance is IFlowCaptureReferenceOperation utf8EnumFcr &&
            _captures.TryGetValue(utf8EnumFcr.Id, out var utf8EnumCap) &&
            utf8EnumCap.Kind == CaptureKind.Utf8ScanEnumerator)
        {
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.SpanSlice,
                destPlace,
                null,
                false,
                new MirOperand[]
                {
                    new MirOperand.CopyOrMove(MirPlace.Local(utf8EnumCap.PrimaryLocalId), utf8EnumCap.MirType, span),
                    new MirOperand.CopyOrMove(MirPlace.Local(utf8EnumCap.TertiaryLocalId), MirType.Int32, span),
                    new MirOperand.CopyOrMove(MirPlace.Local(utf8EnumCap.QuaternaryLocalId), MirType.Int32, span),
                },
                Array.Empty<MirPlace>(),
                utf8EnumCap.MirType,
                span));
            return true;
        }

        if (prop.Property.Name == "Current" &&
            prop.Instance is IFlowCaptureReferenceOperation enumFcr &&
            _captures.TryGetValue(enumFcr.Id, out var enumCap) &&
            enumCap.Kind == CaptureKind.SpanEnumerator)
        {
            int refTmp = CreateTempRefLocal(enumCap.MirType, isMutable: false, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.SpanIndexRef,
                null,
                refTmp,
                false,
                new MirOperand[] { new MirOperand.CopyOrMove(MirPlace.Local(enumCap.SecondaryLocalId), MirType.Int32, span) },
                new[] { MirPlace.Local(enumCap.PrimaryLocalId) },
                new MirType.RefType(enumCap.MirType, false),
                span));
            var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(enumCap.MirType) });
            _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, enumCap.MirType, span)), span));
            return true;
        }

        if (prop.Property.Name == "None" && owner?.Name == "Prelude" && owner.ContainingNamespace?.ToDisplayString() == "Vibranium")
        {
            if (targetMirType is MirType.OptionType opt)
            {
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.LiftOptionNone(opt.Inner), span));
            }
            else
            {
                _currentStmts.Add(new MirStatement.Assign(
                    destPlace,
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.NoneUnit), MirType.NoneUnit, span)),
                    span));
            }
            return true;
        }

        if ((VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Option") ||
             VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "OptionRef") ||
             VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "OptionMut")) &&
            prop.Instance is not null &&
            prop.Property.Name is "IsSome" or "IsEmpty")
        {
            var optMirT = MapMirType(SubstituteType(prop.Instance.Type));
            var optPlace = LowerPlaceOrMaterializeTemp(prop.Instance, optMirT, isMutableAccess: false, span);

            if (prop.Property.Name == "IsSome")
            {
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.OptionIsSome, destPlace, null, false, Array.Empty<MirOperand>(), new[] { optPlace }, MirType.Bool, span));
            }
            else
            {
                int isSomeTmp = CreateTempLocal(MirType.Bool, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.OptionIsSome, MirPlace.Local(isSomeTmp), null, false, Array.Empty<MirOperand>(), new[] { optPlace }, MirType.Bool, span));
                _currentStmts.Add(new MirStatement.Assign(
                    destPlace,
                    new MirRvalue.Unary(MirUnaryOp.Not, new MirOperand.CopyOrMove(MirPlace.Local(isSomeTmp), MirType.Bool, span), MirType.Bool, false),
                    span));
            }
            return true;
        }

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Result") &&
            prop.Instance is not null &&
            prop.Property.Name == "IsOk")
        {
            var resMirT = MapMirType(SubstituteType(prop.Instance.Type));
            var resPlace = LowerPlaceOrMaterializeTemp(prop.Instance, resMirT, isMutableAccess: false, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                MirIntrinsicKind.ResultIsOk, destPlace, null, false, Array.Empty<MirOperand>(), new[] { resPlace }, MirType.Bool, span));
            return true;
        }

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec") && prop.Instance is not null)
        {
            var vecMirT = MapMirType(SubstituteType(prop.Instance.Type));
            if (prop.Property.Name is "Count" or "Capacity")
            {
                var vecPlace = LowerPlaceOrMaterializeTemp(prop.Instance, vecMirT, isMutableAccess: false, span);
                var kind = prop.Property.Name == "Count" ? MirIntrinsicKind.VecCount : MirIntrinsicKind.VecCapacity;
                _currentStmts.Add(new MirStatement.Intrinsic(
                    kind, destPlace, null, false, Array.Empty<MirOperand>(), new[] { vecPlace }, MirType.Int32, span));
                return true;
            }
            if (prop.Property.IsIndexer && prop.Arguments.Length == 1)
            {
                var elemT = ((MirType.VecType)vecMirT).Element;
                var vecPlace = LowerPlaceOrMaterializeTemp(prop.Instance, vecMirT, isMutableAccess: false, span);
                vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirT, isMutable: false, isTwoPhaseBorrow: false, span);
                var idxOp = LowerOperand(prop.Arguments[0].Value, MirType.Int32);
                int refTmp = CreateTempRefLocal(elemT, isMutable: false, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.VecIndexRef, null, refTmp, false, new[] { idxOp }, new[] { vecPlace }, new MirType.RefType(elemT, false), span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, elemT, span)), span));
                return true;
            }
        }

        if (VibraniumSymbolHelpers.IsSpanType(owner, out var spanElem, out bool spanRo) && prop.Instance is not null)
        {
            var elemT = MapMirType(SubstituteType(spanElem));
            var spanMirT = new MirType.SpanType(elemT, spanRo);
            if (prop.Property.Name == "Length")
            {
                var sPlace = LowerPlaceOrMaterializeTemp(prop.Instance, spanMirT, isMutableAccess: false, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanLength, destPlace, null, false, Array.Empty<MirOperand>(), new[] { sPlace }, MirType.Int32, span));
                return true;
            }
            if (prop.Property.Name == "IsEmpty")
            {
                var sPlace = LowerPlaceOrMaterializeTemp(prop.Instance, spanMirT, isMutableAccess: false, span);
                int lenTmp = CreateTempLocal(MirType.Int32, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanLength, MirPlace.Local(lenTmp), null, false, Array.Empty<MirOperand>(), new[] { sPlace }, MirType.Int32, span));
                _currentStmts.Add(new MirStatement.Assign(
                    destPlace,
                    new MirRvalue.Binary(
                        MirBinaryOp.Eq,
                        new MirOperand.CopyOrMove(MirPlace.Local(lenTmp), MirType.Int32, span),
                        new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span),
                        MirType.Bool,
                        false),
                    span));
                return true;
            }
            if (prop.Property.IsIndexer && prop.Arguments.Length == 1)
            {
                var sPlace = LowerPlaceOrMaterializeTemp(prop.Instance, spanMirT, isMutableAccess: false, span);
                sPlace = ReserveReceiverPlaceBeforeArgs(sPlace, spanMirT, isMutable: false, isTwoPhaseBorrow: false, span);
                var idxOp = LowerOperand(prop.Arguments[0].Value, MirType.Int32);
                int refTmp = CreateTempRefLocal(elemT, isMutable: false, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanIndexRef, null, refTmp, false, new[] { idxOp }, new[] { sPlace }, new MirType.RefType(elemT, false), span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, elemT, span)), span));
                return true;
            }
        }

        if (prop.Property.GetMethod is { } getter &&
            owner is INamedTypeSymbol namedOwner &&
            IsInCompilationOrVibraniumStd(namedOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(getter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueTypeInstantiation(namedOwner);

            var callArgs = new List<MirCallArg>();
            MirPassKind? propRecvPass = null;
            if (!substGetter.IsStatic && prop.Instance is not null)
            {
                propRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                bool isMutPropRecv = propRecvPass == MirPassKind.ImplicitThisMut;
                var recvMirT = MapMirType(SubstituteType(prop.Instance.Type));
                var wrapperRead = ReadsReceiverInPlace(substGetter, prop.Instance, prop.Arguments.Length) ? BeginWrapperRead(prop.Instance) : null;
                var recvPlace = LowerPlaceOrMaterializeTemp(prop.Instance, recvMirT, isMutPropRecv, span, isTwoPhaseBorrow: isMutPropRecv && prop.Arguments.Length > 0);
                EndWrapperRead(wrapperRead);
                if (prop.Arguments.Length > 0)
                {
                    recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: isMutPropRecv, isTwoPhaseBorrow: isMutPropRecv, span);
                }
                callArgs.Add(WithCallHold(new MirCallArg(propRecvPass.Value, null, recvPlace, span), wrapperRead));
            }
            callArgs.AddRange(OrderCallArguments(prop.Arguments, substGetter.Parameters, span, materializeByValueTemps: propRecvPass == MirPassKind.ImplicitThisMut));

            string key = VibraniumSymbolHelpers.GetMethodKey(substGetter);
            if (substGetter.ReturnsByRef || substGetter.ReturnsByRefReadonly)
            {
                bool refMut = substGetter.ReturnsByRef && !substGetter.ReturnsByRefReadonly;
                int refTmp = CreateTempRefLocal(targetMirType, refMut, span);
                _currentStmts.Add(new MirStatement.Call(null, refTmp, refMut, key, callArgs, span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(targetMirType) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, targetMirType, span)), span));
            }
            else
            {
                _currentStmts.Add(new MirStatement.Call(destPlace, null, false, key, callArgs, span));
            }
            return true;
        }

        return false;
    }

    private bool TryEmitImplicitRangeOrIndexInto(
        IImplicitIndexerReferenceOperation implIdx,
        MirPlace destPlace,
        MirType targetMirType,
        VibraniumSpan span)
    {
        var recvTypeSym = SubstituteType(implIdx.Instance.Type);
        bool isVec = VibraniumSymbolHelpers.IsVibraniumCoreType(recvTypeSym, "Vec");
        bool isSpan = VibraniumSymbolHelpers.IsSpanType(recvTypeSym, out var spanElem, out _);
        var argTypeSym = SubstituteType(implIdx.Argument.Type);
        var recvMirT = MapMirType(recvTypeSym);

        if (isVec || isSpan)
        {
            if (argTypeSym?.ToDisplayString() == "System.Range")
            {
                var recvPlace = isVec
                    ? LowerPlace(implIdx.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: false, span);
                bool mutableView = isVec || recvMirT is MirType.SpanType { IsReadOnly: false };
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: mutableView, isTwoPhaseBorrow: mutableView, span);

                var (startOp, lenOp) = LowerRangeBounds(implIdx.Argument, recvPlace, isVec, span);
                var kind = isVec ? MirIntrinsicKind.VecSliceMut : MirIntrinsicKind.SpanSlice;
                _currentStmts.Add(new MirStatement.Intrinsic(
                    kind, destPlace, null, false, new[] { startOp, lenOp }, new[] { recvPlace }, targetMirType, span));
                return true;
            }

            if (argTypeSym?.ToDisplayString() == "System.Index")
            {
                var elemT = isVec ? ((MirType.VecType)recvMirT).Element : MapMirType(SubstituteType(spanElem));
                var recvPlace = LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: false, span);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: false, isTwoPhaseBorrow: false, span);
                var idxOp = LowerSystemIndex(implIdx.Argument, recvPlace, isVec, span);
                int refTmp = CreateTempRefLocal(elemT, isMutable: false, span);
                var kind = isVec ? MirIntrinsicKind.VecIndexRef : MirIntrinsicKind.SpanIndexRef;
                _currentStmts.Add(new MirStatement.Intrinsic(
                    kind, null, refTmp, false, new[] { idxOp }, new[] { recvPlace }, new MirType.RefType(elemT, false), span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, elemT, span)), span));
                return true;
            }

            return false;
        }

        if (argTypeSym?.ToDisplayString() == "System.Range" &&
            implIdx.IndexerSymbol is IMethodSymbol sliceMethod &&
            implIdx.LengthSymbol is { } rangeLenSym &&
            sliceMethod.ContainingType is INamedTypeSymbol sliceOwner &&
            IsInCompilationOrVibraniumStd(sliceOwner.ContainingAssembly))
        {
            var substSlice = SubstituteMethod(sliceMethod);
            _enqueueMethodInstantiation(substSlice);
            _enqueueTypeInstantiation(sliceOwner);

            var sliceRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSlice);
            bool isMutSliceRecv = sliceRecvPass == MirPassKind.ImplicitThisMut;
            bool needMutRecv = isMutSliceRecv || (RangeReadsTotalLength(implIdx.Argument) && IsLengthSymbolMutating(rangeLenSym));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implIdx.Instance.Syntax);
            var recvPlace = needMutRecv
                ? LowerPlace(implIdx.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

            var (startOp, lenOp) = LowerRangeBoundsGeneric(implIdx.Argument, recvPlace, rangeLenSym, span);
            var callArgs = new List<MirCallArg>(3)
            {
                new(sliceRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, startOp, null, span),
                new(MirPassKind.ByValue, lenOp, null, span),
            };
            string key = VibraniumSymbolHelpers.GetMethodKey(substSlice);
            if (substSlice.ReturnsByRef || substSlice.ReturnsByRefReadonly)
            {
                bool refMut = substSlice.ReturnsByRef && !substSlice.ReturnsByRefReadonly;
                int refTmp = CreateTempRefLocal(targetMirType, refMut, span);
                _currentStmts.Add(new MirStatement.Call(null, refTmp, refMut, key, callArgs, span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(targetMirType) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, targetMirType, span)), span));
            }
            else
            {
                _currentStmts.Add(new MirStatement.Call(destPlace, null, false, key, callArgs, span));
            }
            return true;
        }

        if (argTypeSym?.ToDisplayString() == "System.Index" &&
            implIdx.IndexerSymbol is IPropertySymbol { GetMethod: { } idxGetter } &&
            implIdx.LengthSymbol is { } idxLenSym &&
            idxGetter.ContainingType is INamedTypeSymbol idxOwner &&
            IsInCompilationOrVibraniumStd(idxOwner.ContainingAssembly))
        {
            var substGetter = SubstituteMethod(idxGetter);
            _enqueueMethodInstantiation(substGetter);
            _enqueueTypeInstantiation(idxOwner);

            var idxRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
            bool isMutIdxRecv = idxRecvPass == MirPassKind.ImplicitThisMut;
            bool needMutRecv = isMutIdxRecv || (IsFromEndIndexOperation(implIdx.Argument) && IsLengthSymbolMutating(idxLenSym));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implIdx.Instance.Syntax);
            var recvPlace = needMutRecv
                ? LowerPlace(implIdx.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

            var idxOp = LowerSystemIndexGeneric(implIdx.Argument, recvPlace, idxLenSym, span);
            var callArgs = new List<MirCallArg>(2)
            {
                new(idxRecvPass, null, recvPlace, recvSpan),
                new(MirPassKind.ByValue, idxOp, null, span),
            };
            string key = VibraniumSymbolHelpers.GetMethodKey(substGetter);
            if (substGetter.ReturnsByRef || substGetter.ReturnsByRefReadonly)
            {
                bool refMut = substGetter.ReturnsByRef && !substGetter.ReturnsByRefReadonly;
                int refTmp = CreateTempRefLocal(targetMirType, refMut, span);
                _currentStmts.Add(new MirStatement.Call(null, refTmp, refMut, key, callArgs, span));
                var deref = new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(targetMirType) });
                _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(new MirOperand.CopyOrMove(deref, targetMirType, span)), span));
            }
            else
            {
                _currentStmts.Add(new MirStatement.Call(destPlace, null, false, key, callArgs, span));
            }
            return true;
        }

        return false;
    }

    private bool IsLengthSymbolMutating(ISymbol lengthSymbol) =>
        lengthSymbol is IPropertySymbol { GetMethod: { } lg } &&
        VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(lg)) == MirPassKind.ImplicitThisMut;

    private static bool IsFromEndIndexOperation(IOperation? indexOp)
    {
        if (indexOp is null) return false;
        if (indexOp is IConversionOperation conv && conv.Operand.Type?.SpecialType == SpecialType.System_Int32)
        {
            return false;
        }
        if (indexOp is IObjectCreationOperation oc && oc.Arguments.Length >= 1)
        {
            return oc.Arguments.Length >= 2 && oc.Arguments[1].Value.ConstantValue is { HasValue: true, Value: true };
        }
        return indexOp.Syntax is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.IndexExpression);
    }

    private static bool RangeReadsTotalLength(IOperation rangeArg) =>
        rangeArg is IRangeOperation r &&
        (r.RightOperand is null || IsFromEndIndexOperation(r.LeftOperand) || IsFromEndIndexOperation(r.RightOperand));

    private (MirOperand Start, MirOperand Length) LowerRangeBounds(
        IOperation rangeArg,
        MirPlace recvPlace,
        bool isVec,
        VibraniumSpan span)
    {
        if (rangeArg is not IRangeOperation rangeOp)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "Non-literal System.Range variables are not supported in Vibranium; write 'a..b' inline.", span);
            var z = new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
            return (z, z);
        }

        MirOperand? cachedTotal = null;
        return LowerRangeBoundsWithDeferredTotal(rangeOp, () =>
        {
            if (cachedTotal is not null) return cachedTotal;
            int totTmp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Intrinsic(
                isVec ? MirIntrinsicKind.VecCount : MirIntrinsicKind.SpanLength,
                MirPlace.Local(totTmp),
                null,
                false,
                Array.Empty<MirOperand>(),
                new[] { recvPlace },
                MirType.Int32,
                span));
            cachedTotal = new MirOperand.CopyOrMove(MirPlace.Local(totTmp), MirType.Int32, span);
            return cachedTotal;
        }, span);
    }

    private (MirOperand Start, MirOperand Length) LowerRangeBoundsGeneric(
        IOperation rangeArg,
        MirPlace recvPlace,
        ISymbol lengthSymbol,
        VibraniumSpan span)
    {
        if (rangeArg is not IRangeOperation rangeOp)
        {
            EmitDiag(VibraniumDiagnosticIds.Unsupported, "Non-literal System.Range variables are not supported in Vibranium; write 'a..b' inline.", span);
            var z = new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
            return (z, z);
        }

        MirOperand? cachedTotal = null;
        return LowerRangeBoundsWithDeferredTotal(rangeOp, () =>
        {
            cachedTotal ??= EmitReadLengthSymbol(recvPlace, lengthSymbol, span);
            return cachedTotal;
        }, span);
    }

    private (MirOperand Start, MirOperand Length) LowerRangeBoundsWithDeferredTotal(
        IRangeOperation rangeOp,
        Func<MirOperand> getTotalOnce,
        VibraniumSpan span)
    {
        bool leftFromEnd = false;
        MirOperand leftRaw = rangeOp.LeftOperand is not null
            ? LowerIndexRawOperand(rangeOp.LeftOperand, span, out leftFromEnd)
            : new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);

        bool rightFromEnd = false;
        MirOperand? rightRaw = rangeOp.RightOperand is not null
            ? LowerIndexRawOperand(rangeOp.RightOperand, span, out rightFromEnd)
            : null;

        MirOperand? totalOp = (leftFromEnd || rightFromEnd || rightRaw is null)
            ? getTotalOnce()
            : null;

        MirOperand startOp;
        if (leftFromEnd)
        {
            int subStart = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(subStart),
                new MirRvalue.Binary(MirBinaryOp.Sub, totalOp!, leftRaw, MirType.Int32, false),
                span));
            startOp = new MirOperand.CopyOrMove(MirPlace.Local(subStart), MirType.Int32, span);
        }
        else
        {
            startOp = leftRaw;
        }

        MirOperand endOp;
        if (rightRaw is null)
        {
            endOp = totalOp!;
        }
        else if (rightFromEnd)
        {
            int subEnd = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(subEnd),
                new MirRvalue.Binary(MirBinaryOp.Sub, totalOp!, rightRaw, MirType.Int32, false),
                span));
            endOp = new MirOperand.CopyOrMove(MirPlace.Local(subEnd), MirType.Int32, span);
        }
        else
        {
            endOp = rightRaw;
        }

        int lenTmp = CreateTempLocal(MirType.Int32, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(lenTmp),
            new MirRvalue.Binary(MirBinaryOp.Sub, endOp, startOp, MirType.Int32, false),
            span));
        return (startOp, new MirOperand.CopyOrMove(MirPlace.Local(lenTmp), MirType.Int32, span));
    }

    private MirOperand LowerIndexRawOperand(IOperation indexOp, VibraniumSpan span, out bool fromEnd)
    {
        fromEnd = false;
        if (indexOp is IConversionOperation conv && conv.Operand.Type?.SpecialType == SpecialType.System_Int32)
        {
            return SnapshotCopyOperandIfNeeded(LowerOperand(conv.Operand, MirType.Int32), MirType.Int32, span);
        }

        if (indexOp is IObjectCreationOperation oc && oc.Arguments.Length >= 1)
        {
            var valOp = SnapshotCopyOperandIfNeeded(LowerOperand(oc.Arguments[0].Value, MirType.Int32), MirType.Int32, span);
            fromEnd = oc.Arguments.Length >= 2 && oc.Arguments[1].Value.ConstantValue is { HasValue: true, Value: true };
            return valOp;
        }

        if (indexOp.Syntax is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.IndexExpression))
        {
            var child = indexOp.ChildOperations.FirstOrDefault();
            if (child is not null)
            {
                fromEnd = true;
                return SnapshotCopyOperandIfNeeded(LowerOperand(child, MirType.Int32), MirType.Int32, span);
            }
        }

        EmitDiag(VibraniumDiagnosticIds.Unsupported, "Arbitrary System.Index values are not supported in Vibranium; use int or '^k'.", span);
        return new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
    }

    private MirOperand LowerSystemIndex(IOperation indexOp, MirPlace recvPlace, bool isVec, VibraniumSpan span)
    {
        MirOperand rawOp = LowerIndexRawOperand(indexOp, span, out bool fromEnd);
        if (!fromEnd) return rawOp;

        int totTmp = CreateTempLocal(MirType.Int32, span);
        _currentStmts.Add(new MirStatement.Intrinsic(
            isVec ? MirIntrinsicKind.VecCount : MirIntrinsicKind.SpanLength,
            MirPlace.Local(totTmp),
            null,
            false,
            Array.Empty<MirOperand>(),
            new[] { recvPlace },
            MirType.Int32,
            span));
        var totOp = new MirOperand.CopyOrMove(MirPlace.Local(totTmp), MirType.Int32, span);
        int subTmp = CreateTempLocal(MirType.Int32, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(subTmp),
            new MirRvalue.Binary(MirBinaryOp.Sub, totOp, rawOp, MirType.Int32, false),
            span));
        return new MirOperand.CopyOrMove(MirPlace.Local(subTmp), MirType.Int32, span);
    }

    private MirOperand EmitReadLengthSymbol(MirPlace recvPlace, ISymbol lengthSymbol, VibraniumSpan span)
    {
        if (lengthSymbol is IPropertySymbol { GetMethod: { } lenGetter } &&
            lenGetter.ContainingType is INamedTypeSymbol lenOwner &&
            IsInCompilationOrVibraniumStd(lenOwner.ContainingAssembly))
        {
            var substLenGetter = SubstituteMethod(lenGetter);
            _enqueueMethodInstantiation(substLenGetter);
            _enqueueTypeInstantiation(lenOwner);
            var lenPass = VibraniumSymbolHelpers.GetReceiverPassKind(substLenGetter);
            int totTmp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Call(
                MirPlace.Local(totTmp),
                null,
                false,
                VibraniumSymbolHelpers.GetMethodKey(substLenGetter),
                new[] { new MirCallArg(lenPass, null, recvPlace, span) },
                span));
            return new MirOperand.CopyOrMove(MirPlace.Local(totTmp), MirType.Int32, span);
        }

        if (lengthSymbol is IFieldSymbol lenField &&
            lenField.ContainingType is { } fldOwner &&
            IsInCompilationOrVibraniumStd(fldOwner.ContainingAssembly))
        {
            bool throughHeap = fldOwner.TypeKind == TypeKind.Class;
            int fieldIndex = GetFieldDeclarationIndex(lenField);
            var projs = new List<MirProjection>(recvPlace.Projections.Count + 1);
            projs.AddRange(recvPlace.Projections);
            projs.Add(new MirProjection.Field(lenField.Name, fieldIndex, MirType.Int32, throughHeap, lenField.IsReadOnly));
            int totTmp = CreateTempLocal(MirType.Int32, span);
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(totTmp),
                new MirRvalue.Use(new MirOperand.CopyOrMove(new MirPlace(recvPlace.LocalId, projs), MirType.Int32, span)),
                span));
            return new MirOperand.CopyOrMove(MirPlace.Local(totTmp), MirType.Int32, span);
        }

        EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Length member '{lengthSymbol.Name}' is not supported in Vibranium.", span);
        return new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
    }

    private MirOperand LowerSystemIndexGeneric(
        IOperation indexOp,
        MirPlace recvPlace,
        ISymbol lengthSymbol,
        VibraniumSpan span)
    {
        MirOperand rawOp = LowerIndexRawOperand(indexOp, span, out bool fromEnd);
        if (!fromEnd) return rawOp;

        var totOp = EmitReadLengthSymbol(recvPlace, lengthSymbol, span);
        int subTmp = CreateTempLocal(MirType.Int32, span);
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(subTmp),
            new MirRvalue.Binary(MirBinaryOp.Sub, totOp, rawOp, MirType.Int32, false),
            span));
        return new MirOperand.CopyOrMove(MirPlace.Local(subTmp), MirType.Int32, span);
    }

    private bool TryEmitRefProducingOperationInto(IOperation valOp, int refDestLocalId, bool isMut, VibraniumSpan span, bool isTwoPhaseBorrow = false)
    {
        switch (valOp)
        {
            case IPropertyReferenceOperation { Property.Name: "Current", Instance: IFlowCaptureReferenceOperation enumFcr }
                when _captures.TryGetValue(enumFcr.Id, out var enumCap) && enumCap.Kind == CaptureKind.SpanEnumerator:
            {
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.SpanIndexRef,
                    null,
                    refDestLocalId,
                    isMut,
                    new MirOperand[] { new MirOperand.CopyOrMove(MirPlace.Local(enumCap.SecondaryLocalId), MirType.Int32, span) },
                    new[] { MirPlace.Local(enumCap.PrimaryLocalId) },
                    new MirType.RefType(enumCap.MirType, isMut),
                    span,
                    isTwoPhaseBorrow));
                return true;
            }

            case IPropertyReferenceOperation { Property.IsIndexer: true, Instance: { } idxInst } propIdx when propIdx.Arguments.Length == 1:
            {
                var owner = SubstituteType(propIdx.Property.ContainingType);
                if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec"))
                {
                    var vecMirT = (MirType.VecType)MapMirType(SubstituteType(idxInst.Type));
                    var vecPlace = isMut ? LowerPlace(idxInst, isMutableAccess: true, isTwoPhaseBorrow: true) : LowerPlaceOrMaterializeTemp(idxInst, vecMirT, false, span);
                    vecPlace = ReserveReceiverPlaceBeforeArgs(vecPlace, vecMirT, isMutable: isMut, isTwoPhaseBorrow: isMut, span);
                    var idxOp = LowerOperand(propIdx.Arguments[0].Value, MirType.Int32);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.VecIndexRef, null, refDestLocalId, isMut, new[] { idxOp }, new[] { vecPlace }, new MirType.RefType(vecMirT.Element, isMut), span, isTwoPhaseBorrow));
                    return true;
                }
                if (VibraniumSymbolHelpers.IsSpanType(owner, out var spanElem, out bool spanRo))
                {
                    var elemT = MapMirType(SubstituteType(spanElem));
                    var spanMirT = new MirType.SpanType(elemT, spanRo);
                    var sPlace = LowerPlaceOrMaterializeTemp(idxInst, spanMirT, isMutableAccess: isMut, span, isTwoPhaseBorrow: isTwoPhaseBorrow);
                    bool effectiveMut = isMut && !spanRo;
                    sPlace = ReserveReceiverPlaceBeforeArgs(sPlace, spanMirT, isMutable: effectiveMut, isTwoPhaseBorrow: effectiveMut, span);
                    var idxOp = LowerOperand(propIdx.Arguments[0].Value, MirType.Int32);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        MirIntrinsicKind.SpanIndexRef, null, refDestLocalId, effectiveMut, new[] { idxOp }, new[] { sPlace }, new MirType.RefType(elemT, effectiveMut), span, isTwoPhaseBorrow));
                    return true;
                }
                if (propIdx.Property.GetMethod is { } idxGetter &&
                    (idxGetter.ReturnsByRef || idxGetter.ReturnsByRefReadonly) &&
                    owner is INamedTypeSymbol namedOwner &&
                    IsInCompilationOrVibraniumStd(namedOwner.ContainingAssembly))
                {
                    var substGetter = SubstituteMethod(idxGetter);
                    _enqueueMethodInstantiation(substGetter);
                    _enqueueTypeInstantiation(namedOwner);
                    var recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                    bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
                    var recvMirT = MapMirType(SubstituteType(idxInst.Type));
                    var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(idxInst.Syntax);
                    var wrapperRead = ReadsReceiverInPlace(substGetter, idxInst, propIdx.Arguments.Length) ? BeginWrapperRead(idxInst) : null;
                    var recvPlace = isMutRecv
                        ? LowerPlace(idxInst, isMutableAccess: true, isTwoPhaseBorrow: true)
                        : LowerPlaceOrMaterializeTemp(idxInst, recvMirT, isMutableAccess: false, recvSpan);
                    EndWrapperRead(wrapperRead);
                    recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: isMutRecv, isTwoPhaseBorrow: isMutRecv, recvSpan);
                    var callArgs = new List<MirCallArg> { WithCallHold(new(recvPass, null, recvPlace, recvSpan), wrapperRead) };
                    callArgs.AddRange(OrderCallArguments(propIdx.Arguments, substGetter.Parameters, span, materializeByValueTemps: isMutRecv));
                    bool retMut = substGetter.ReturnsByRef && !substGetter.ReturnsByRefReadonly;
                    _currentStmts.Add(new MirStatement.Call(null, refDestLocalId, retMut, VibraniumSymbolHelpers.GetMethodKey(substGetter), callArgs, span));
                    return true;
                }
                break;
            }

            case IPropertyReferenceOperation { Property.GetMethod: { } refPropGetter } propRef
                when (refPropGetter.ReturnsByRef || refPropGetter.ReturnsByRefReadonly) &&
                     refPropGetter.ContainingType is INamedTypeSymbol refPropOwner &&
                     IsInCompilationOrVibraniumStd(refPropOwner.ContainingAssembly):
            {
                var substGetter = SubstituteMethod(refPropGetter);
                _enqueueMethodInstantiation(substGetter);
                _enqueueTypeInstantiation(refPropOwner);
                var callArgs = new List<MirCallArg>();
                MirPassKind? recvPass = null;
                if (!substGetter.IsStatic && propRef.Instance is not null)
                {
                    recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                    bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
                    var recvMirT = MapMirType(SubstituteType(propRef.Instance.Type));
                    var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(propRef.Instance.Syntax);
                    var wrapperRead = ReadsReceiverInPlace(substGetter, propRef.Instance, propRef.Arguments.Length) ? BeginWrapperRead(propRef.Instance) : null;
                    var recvPlace = isMutRecv
                        ? LowerPlace(propRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: propRef.Arguments.Length > 0)
                        : LowerPlaceOrMaterializeTemp(propRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
                    EndWrapperRead(wrapperRead);
                    if (propRef.Arguments.Length > 0)
                    {
                        recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: isMutRecv, isTwoPhaseBorrow: isMutRecv, recvSpan);
                    }
                    callArgs.Add(WithCallHold(new MirCallArg(recvPass.Value, null, recvPlace, recvSpan), wrapperRead));
                }
                callArgs.AddRange(OrderCallArguments(propRef.Arguments, substGetter.Parameters, span, materializeByValueTemps: recvPass == MirPassKind.ImplicitThisMut));
                bool retMut = substGetter.ReturnsByRef && !substGetter.ReturnsByRefReadonly;
                _currentStmts.Add(new MirStatement.Call(null, refDestLocalId, retMut, VibraniumSymbolHelpers.GetMethodKey(substGetter), callArgs, span));
                return true;
            }

            case IImplicitIndexerReferenceOperation implIdx when SubstituteType(implIdx.Argument.Type)?.ToDisplayString() == "System.Index":
            {
                var recvType = SubstituteType(implIdx.Instance.Type);
                bool isVec = VibraniumSymbolHelpers.IsVibraniumCoreType(recvType, "Vec");
                bool isSpan = VibraniumSymbolHelpers.IsSpanType(recvType, out var spanElem, out bool spanRo);
                if (isVec || isSpan)
                {
                    var recvMirT = MapMirType(recvType);
                    var elemT = isVec ? ((MirType.VecType)recvMirT).Element : MapMirType(SubstituteType(spanElem));
                    var recvPlace = (isVec && isMut)
                        ? LowerPlace(implIdx.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                        : LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: isMut, span, isTwoPhaseBorrow: isTwoPhaseBorrow);
                    bool effectiveMut = isMut && !(isSpan && spanRo);
                    recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: effectiveMut, isTwoPhaseBorrow: effectiveMut, span);
                    var idxOp = LowerSystemIndex(implIdx.Argument, recvPlace, isVec, span);
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        isVec ? MirIntrinsicKind.VecIndexRef : MirIntrinsicKind.SpanIndexRef,
                        null,
                        refDestLocalId,
                        effectiveMut,
                        new[] { idxOp },
                        new[] { recvPlace },
                        new MirType.RefType(elemT, effectiveMut),
                        span,
                        isTwoPhaseBorrow));
                    return true;
                }
                if (implIdx.IndexerSymbol is IPropertySymbol { GetMethod: { } implRefGetter } &&
                    (implRefGetter.ReturnsByRef || implRefGetter.ReturnsByRefReadonly) &&
                    implIdx.LengthSymbol is { } implRefLenSym &&
                    implRefGetter.ContainingType is INamedTypeSymbol implRefOwner &&
                    IsInCompilationOrVibraniumStd(implRefOwner.ContainingAssembly))
                {
                    var substGetter = SubstituteMethod(implRefGetter);
                    _enqueueMethodInstantiation(substGetter);
                    _enqueueTypeInstantiation(implRefOwner);
                    var recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substGetter);
                    bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
                    bool needMutRecv = isMutRecv || (IsFromEndIndexOperation(implIdx.Argument) && IsLengthSymbolMutating(implRefLenSym));
                    var recvMirT = MapMirType(recvType);
                    var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implIdx.Instance.Syntax);
                    var recvPlace = needMutRecv
                        ? LowerPlace(implIdx.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                        : LowerPlaceOrMaterializeTemp(implIdx.Instance, recvMirT, isMutableAccess: false, recvSpan);
                    recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);
                    var idxOp = LowerSystemIndexGeneric(implIdx.Argument, recvPlace, implRefLenSym, span);
                    var callArgs = new List<MirCallArg>(2)
                    {
                        new(recvPass, null, recvPlace, recvSpan),
                        new(MirPassKind.ByValue, idxOp, null, span),
                    };
                    bool retMut = substGetter.ReturnsByRef && !substGetter.ReturnsByRefReadonly;
                    _currentStmts.Add(new MirStatement.Call(null, refDestLocalId, retMut, VibraniumSymbolHelpers.GetMethodKey(substGetter), callArgs, span));
                    return true;
                }
                break;
            }

            case IInvocationOperation { TargetMethod.Name: "UnwrapMut", Instance: { } optInst } unwrapMutInv
                when VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(unwrapMutInv.TargetMethod.ContainingType), "Option"):
            {
                var optT = (MirType.OptionType)MapMirType(SubstituteType(optInst.Type));
                if (_module.IsLinearResource(optT.Inner))
                {
                    EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "'Option.UnwrapMut' cannot be used on linear IDisposable resources.", span);
                }
                var slotPlace = LowerPlace(optInst, isMutableAccess: true, isTwoPhaseBorrow: isTwoPhaseBorrow);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.OptionUnwrapMut, null, refDestLocalId, true, Array.Empty<MirOperand>(), new[] { slotPlace }, new MirType.RefType(optT.Inner, true), span, isTwoPhaseBorrow));
                return true;
            }

            case IInvocationOperation refCallInv when refCallInv.TargetMethod.ReturnsByRef || refCallInv.TargetMethod.ReturnsByRefReadonly:
            {
                var method = SubstituteMethod(refCallInv.TargetMethod);
                var owner = method.ContainingType;
                if (owner is not null && IsInCompilationOrVibraniumStd(owner.ContainingAssembly))
                {
                    _enqueueMethodInstantiation(method);
                    _enqueueTypeInstantiation(owner);

                    var callArgs = new List<MirCallArg>();
                    MirPassKind? recvPass = null;
                    if (method.IsStatic && refCallInv.Instance is not null && method.Parameters.Length > 0)
                    {
                        callArgs.AddRange(OrderCallArgumentsWithReducedReceiver(refCallInv.Instance, refCallInv.Arguments, method.Parameters, span));
                    }
                    else
                    {
                        if (!method.IsStatic && refCallInv.Instance is not null)
                        {
                            recvPass = VibraniumSymbolHelpers.GetReceiverPassKind(method);
                            var recvMirT = MapMirType(SubstituteType(refCallInv.Instance.Type));
                            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(refCallInv.Instance.Syntax);
                            bool isMutRecv = recvPass == MirPassKind.ImplicitThisMut;
                            var wrapperRead = ReadsReceiverInPlace(method, refCallInv.Instance, refCallInv.Arguments.Length) ? BeginWrapperRead(refCallInv.Instance) : null;
                            var recvPlace = LowerPlaceOrMaterializeTemp(
                                refCallInv.Instance,
                                recvMirT,
                                isMutRecv,
                                recvSpan,
                                isTwoPhaseBorrow: isMutRecv && refCallInv.Arguments.Length > 0);
                            EndWrapperRead(wrapperRead);
                            if (refCallInv.Arguments.Length > 0)
                            {
                                recvPlace = ReserveReceiverPlaceBeforeArgs(
                                    recvPlace,
                                    recvMirT,
                                    isMutable: isMutRecv,
                                    isTwoPhaseBorrow: isMutRecv,
                                    recvSpan);
                            }
                            callArgs.Add(WithCallHold(new MirCallArg(recvPass.Value, null, recvPlace, recvSpan), wrapperRead));
                        }
                        var explicitArgs = OrderCallArguments(refCallInv.Arguments, method.Parameters, span, recvPass == MirPassKind.ImplicitThisMut);
                        callArgs.AddRange(explicitArgs);
                    }

                    bool retMut = method.ReturnsByRef && !method.ReturnsByRefReadonly;
                    _currentStmts.Add(new MirStatement.Call(null, refDestLocalId, retMut, VibraniumSymbolHelpers.GetMethodKey(method), callArgs, span));
                    return true;
                }
                break;
            }
        }

        return false;
    }

    private static bool IsVecOrSpanIndexerAccess(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        if (op is IPropertyReferenceOperation { Property.IsIndexer: true } prop)
        {
            var owner = prop.Property.ContainingType;
            return VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec") || VibraniumSymbolHelpers.IsSpanType(owner, out _, out _);
        }
        if (op is IImplicitIndexerReferenceOperation impl)
        {
            var recv = impl.Instance.Type;
            return VibraniumSymbolHelpers.IsVibraniumCoreType(recv, "Vec") || VibraniumSymbolHelpers.IsSpanType(recv, out _, out _);
        }
        return false;
    }

    private MirPlace SnapshotAssignmentTarget(MirPlace place, MirType type, VibraniumSpan span)
    {
        if (place.Projections.Count > 0 && place.Projections[0] is MirProjection.Deref)
        {
            // C# evaluates the variable reference before the RHS, which may rebind its ref carrier.
            // Snapshot the pointer, retaining the original referent's loans until the final store.
            int temp = CreateTempRefLocal(type, isMutable: true, span);
            _currentStmts.Add(new MirStatement.BindRef(temp, true, place, span,
                IsTwoPhaseBorrow: true, IsAssignmentTargetSnapshot: true));
            return new MirPlace(temp, new MirProjection[] { new MirProjection.Deref(type) });
        }
        return place.Projections.Any(p => p is MirProjection.Field { ThroughClassHeap: true })
            ? ReserveReceiverPlaceBeforeArgs(place, type, isMutable: true, isTwoPhaseBorrow: true, span)
            : place;
    }

    // F14: C# passes this receiver by reference and reads it after the arguments. A Copy place receiver stays
    // borrowed while they run; a !Copy one is moved out before them, as every consuming receiver is.
    // Call the returned function after lowering the arguments.
    private Func<MirOperand> ReserveReceiverOperandBeforeArgs(IOperation instance, MirType recvT, VibraniumSpan span)
    {
        // A flow capture hands over the hold of a Copy wrapper it read before a branching argument.
        var wrapperRead = BeginWrapperRead(instance);
        if (!CanLowerAsDirectPlace(instance))
        {
            var operand = LowerOperand(instance, recvT);
            EndWrapperRead(wrapperRead);
            return () => KeepCallHold(wrapperRead, operand, span);
        }
        if (!_module.IsCopy(recvT))
        {
            int temp = CreateTempLocal(recvT, span);
            var value = LowerOperand(instance, recvT);
            EndWrapperRead(wrapperRead);
            // A capture that moved the receiver before a branching argument already holds it.
            int? held = UnwrapImplicitIdentityConversion(instance) is IFlowCaptureReferenceOperation fcr &&
                _captures.TryGetValue(fcr.Id, out var capture) ? capture.MovedPlaceLocalId : null;
            if (held is null)
            {
                value = HoldMovedPlace(value, out held);
            }
            _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(temp), new MirRvalue.Use(value), span));
            var moved = new MirOperand.CopyOrMove(MirPlace.Local(temp), recvT, span) { ReleasesMovedPlace = held };
            return () => moved;
        }
        var reservedBase = LowerPlace(instance, isMutableAccess: false);
        EndWrapperRead(wrapperRead);
        var reserved = ReserveReceiverPlaceBeforeArgs(reservedBase, recvT, isMutable: false, isTwoPhaseBorrow: false, span);
        return () => KeepCallHold(wrapperRead, new MirOperand.CopyOrMove(reserved, recvT, span), span);
    }

    // N90: C# reads a Core wrapper's payload (`s.Value`, `k.Value`, `e.Error`) — or a value-type field chain inside it —
    // that is a struct receiver or an `in` argument in place, after the later arguments, while MIR copies it before them.
    // The wrapper stays borrowed until the call (and through any ref/view return): a Copy wrapper by an analysis-only
    // shared hold, a !Copy one, which the copy moves, by a D1-W hold.
    private sealed class CallHold
    {
        public IFieldReferenceOperation? Field;
        public int? Hold;
        public int? Held;
    }

    private readonly Dictionary<IFieldReferenceOperation, CallHold> _pendingWrapperReads = new();

    // Call before lowering `op` and EndWrapperRead after it. A flow capture hands over the read it lowered.
    private CallHold? BeginWrapperRead(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        if (op is IFlowCaptureReferenceOperation fcr && _captures.TryGetValue(fcr.Id, out var capture))
        {
            var captured = capture.Hold;
            capture.Hold = null;
            return captured;
        }
        IFieldReferenceOperation? deepestWrapper = null;
        while (op is IFieldReferenceOperation { Instance: { } inst } field)
        {
            if (IsWrapperFieldUnwrap(field))
            {
                deepestWrapper = field;
            }
            else if (SubstituteType(inst.Type) is not { IsValueType: true })
            {
                break;
            }
            op = UnwrapImplicitIdentityConversion(inst);
        }
        if (deepestWrapper is null)
        {
            return null;
        }
        var read = new CallHold { Field = deepestWrapper };
        _pendingWrapperReads[deepestWrapper] = read;
        return read;
    }

    private void EndWrapperRead(CallHold? read)
    {
        if (read?.Field is { } field) _pendingWrapperReads.Remove(field);
    }

    private MirOperand NoteWrapperRead(IFieldReferenceOperation field, MirOperand wrapper)
    {
        if (!_pendingWrapperReads.TryGetValue(field, out var read) || !_pendingWrapperReads.Remove(field) ||
            wrapper is not MirOperand.CopyOrMove { Place.LocalId: >= 0 } copy)
        {
            return wrapper;
        }
        // A Copy wrapper is read through a snapshot: hold the place that the snapshot copied.
        var place = copy.Place;
        if (_currentStmts.Count > 0 &&
            _currentStmts[_currentStmts.Count - 1] is MirStatement.Assign
            { Target: { Projections.Count: 0 } snapshot, Rvalue: MirRvalue.Use { Operand: MirOperand.CopyOrMove source } } &&
            place.Projections.Count == 0 && snapshot.LocalId == place.LocalId && _module.IsCopy(copy.Type))
        {
            place = source.Place;
        }
        if (place.LocalId < 0 || place.Projections.Count == 0 && _locals[place.LocalId].IsCompilerTemp)
        {
            return wrapper;
        }
        if (!_module.IsCopy(copy.Type))
        {
            return HoldMovedPlace(wrapper, out read.Held);
        }
        int hold = CreateTempRefLocal(copy.Type, isMutable: false, copy.Span, "__wrap_hold", isCallHold: true);
        _currentStmts.Add(new MirStatement.BindRef(hold, false, place, copy.Span));
        read.Hold = hold;
        return wrapper;
    }

    // A built-in reads its receiver operand at the intrinsic, after the arguments, which are values by then.
    private MirOperand KeepCallHold(CallHold? read, MirOperand receiver, VibraniumSpan span)
    {
        KeepCallHold(read, span);
        if (read?.Held is int held && receiver is MirOperand.CopyOrMove cm && cm.ReleasesMovedPlace is null)
        {
            return cm with { ReleasesMovedPlace = held };
        }
        return receiver;
    }

    private void KeepCallHold(CallHold? read, VibraniumSpan span)
    {
        if (read?.Hold is int hold)
        {
            _currentStmts.Add(new MirStatement.KeepAnalysisBorrow(hold, span));
        }
    }

    private static void AddGetterIndexArgs(List<MirCallArg> getArgs, List<MirCallArg> evalIdxArgs)
    {
        // A compound update reuses these arguments for the setter. Only the setter may release the moved wrapper;
        // the getter can read it, but the RHS can still change the slot before the setter reads it again.
        foreach (var arg in evalIdxArgs)
        {
            getArgs.Add(arg.ReleasesMovedPlace is null ? arg : arg with { ReleasesMovedPlace = null });
        }
    }

    private static MirCallArg WithCallHold(MirCallArg arg, CallHold? read) =>
        read is null ? arg : arg with { Hold = read.Hold, ReleasesMovedPlace = read.Held };

    // F14 and N90: C# passes a struct receiver of a `readonly` member by reference and reads it after the arguments
    // (or returns a reference or view borrowing it).
    private bool ReadsReceiverInPlace(IMethodSymbol method, IOperation? instance, int argCount) =>
        instance is not null && !method.IsStatic &&
        (argCount > 0 ||
         ((method.ReturnsByRef || method.ReturnsByRefReadonly ||
           (!method.ReturnsVoid && _module.IsViewLike(MapMirType(SubstituteType(method.ReturnType))))) &&
          (VibraniumSymbolHelpers.HasUnscopedRefAttribute(method) ||
           (method.AssociatedSymbol is IPropertySymbol assocProp && VibraniumSymbolHelpers.HasUnscopedRefAttribute(assocProp))))) &&
        VibraniumSymbolHelpers.GetReceiverPassKind(method) == MirPassKind.ImplicitThisShared &&
        SubstituteType(instance.Type) is { IsValueType: true };

    // D1-W: C# reads a moved receiver's place only after the arguments, so the moved local is held until then: an
    // argument that writes it is VIB003, while any other use of it stays a use of a moved value (VIB001).
    private static MirOperand HoldMovedPlace(MirOperand moved, out int? heldLocalId)
    {
        heldLocalId = null;
        if (moved is not MirOperand.CopyOrMove { Place.Projections.Count: 0 } cm)
        {
            return moved;
        }
        heldLocalId = cm.Place.LocalId;
        return cm with { HoldsMovedPlace = true };
    }

    private MirPlace ReserveReceiverPlaceBeforeArgs(
        MirPlace place,
        MirType mirType,
        bool isMutable,
        bool isTwoPhaseBorrow,
        VibraniumSpan span,
        bool isOut = false)
    {
        int refTmp = CreateTempRefLocal(mirType, isMutable, span);
        bool isOutField = isOut && place.LocalId >= 0 && place.LocalId < _locals.Count &&
            _locals[place.LocalId] is { Type: MirType.StructType, IsRefLocal: false, PassKind: MirPassKind.ByValue or MirPassKind.Out } &&
            place.Projections.Count > 0 && place.Projections.All(p => p is MirProjection.Field { ThroughClassHeap: false });
        _currentStmts.Add(new MirStatement.BindRef(refTmp, isMutable, place, span, isTwoPhaseBorrow,
            IsAssignmentTargetSnapshot: isOutField, IsOut: isOut));
        return new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(mirType) });
    }

    private MirPlace LowerPlaceOrMaterializeTemp(IOperation op, MirType mirType, bool isMutableAccess, VibraniumSpan span, bool isTwoPhaseBorrow = false)
    {
        var unwrapped = UnwrapImplicitIdentityConversion(op);
        if (CanLowerAsDirectPlace(unwrapped))
        {
            return LowerPlace(unwrapped, isMutableAccess, isTwoPhaseBorrow);
        }

        var operand = LowerOperand(op, mirType);
        if (operand is MirOperand.CopyOrMove cm && cm.Place.Projections.Count == 0)
        {
            return cm.Place;
        }
        int temp = CreateTempLocal(mirType, span);
        _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(temp), new MirRvalue.Use(operand), span));
        return MirPlace.Local(temp);
    }

    private bool CanLowerAsDirectPlace(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        if (op.ConstantValue.HasValue) return false;
        return op switch
        {
            ISimpleAssignmentOperation { IsRef: true } => true,
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IInstanceReferenceOperation => true,
            IFlowCaptureReferenceOperation => true,
            IFieldReferenceOperation fr => !fr.Field.IsConst && !fr.ConstantValue.HasValue &&
                fr.Field.ContainingType is not null &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Some") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Ok") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Err"),
            IPropertyReferenceOperation p =>
                p.Property.ReturnsByRef ||
                p.Property.ReturnsByRefReadonly ||
                (p.Property.IsIndexer &&
                 (VibraniumSymbolHelpers.IsVibraniumCoreType(p.Property.ContainingType, "Vec") ||
                  VibraniumSymbolHelpers.IsSpanType(p.Property.ContainingType, out _, out _))),
            IImplicitIndexerReferenceOperation impl =>
                SubstituteType(impl.Argument.Type)?.ToDisplayString() == "System.Index" &&
                (VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(impl.Instance.Type), "Vec") ||
                 VibraniumSymbolHelpers.IsSpanType(SubstituteType(impl.Instance.Type), out _, out _) ||
                 impl.IndexerSymbol is IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true }),
            IInvocationOperation inv => inv.TargetMethod.ReturnsByRef || inv.TargetMethod.ReturnsByRefReadonly,
            _ => false,
        };
    }

    private MirPlace LowerPlace(IOperation op, bool isMutableAccess, bool isTwoPhaseBorrow = false)
    {
        op = UnwrapImplicitIdentityConversion(op);
        var span = VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax);

        switch (op)
        {
            case IDiscardOperation:
            {
                int discardId = CreateTempLocal(MapMirType(SubstituteType(op.Type)), span, "__discard_place");
                return MirPlace.Local(discardId);
            }

            case ISimpleAssignmentOperation { IsRef: true } refAssignment:
            {
                // A ref assignment expression is a variable reference, not a copied value.
                if (LowerSimpleAssignment(refAssignment, wantResult: true) is MirOperand.CopyOrMove result)
                {
                    return result.Place;
                }
                break;
            }

            case ILocalReferenceOperation lr:
            {
                int lid = GetOrCreateLocalId(lr.Local, isPatternBindingOverride: null);
                if (_locals[lid].IsRefLocal)
                {
                    var derefTarget = _locals[lid].Type is MirType.RefType rt ? rt.Target : MapMirType(SubstituteType(lr.Type));
                    return new MirPlace(lid, new MirProjection[] { new MirProjection.Deref(derefTarget) });
                }
                return MirPlace.Local(lid);
            }

            case IParameterReferenceOperation pr:
            {
                if (_paramMap.TryGetValue(pr.Parameter, out int pid) ||
                    _paramMap.TryGetValue(pr.Parameter.OriginalDefinition, out pid))
                {
                    return MirPlace.Local(pid);
                }
                if (_thisLocalId is int capThisId &&
                    TryGetCapturedPrimaryConstructorFieldProjection(pr.Parameter, out var capFieldProj))
                {
                    return new MirPlace(capThisId, new MirProjection[] { capFieldProj });
                }
                int fallback = CreateTempLocal(MapMirType(SubstituteType(pr.Type)), span, pr.Parameter.Name);
                return MirPlace.Local(fallback);
            }

            case IInstanceReferenceOperation ir:
            {
                if (_thisLocalId is int tid)
                {
                    return MirPlace.Local(tid);
                }
                EmitDiag(VibraniumDiagnosticIds.NullOrZeroUnsafe, "Cannot access, capture, or call members on 'this' before constructor finishes initializing all fields.", span);
                var recoveryType = MapMirType(SubstituteType(ir.Type));
                int dummy = CreateTempLocal(recoveryType, span, "__this");
                // The VIB008 above rejects this expression. Give analysis a defined poison
                // value instead of manufacturing an unrelated uninitialized-temp error.
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(dummy),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(recoveryType), recoveryType, span)), span));
                return MirPlace.Local(dummy);
            }

            case IFlowCaptureReferenceOperation fcr:
            {
                if (_captures.TryGetValue(fcr.Id, out var cap))
                {
                    switch (cap.Kind)
                    {
                        case CaptureKind.DirectPlace:
                            return cap.ResolvedDirectPlace ?? LowerPlace(cap.SingleValueOp!, isMutableAccess, isTwoPhaseBorrow);
                        case CaptureKind.ReservedPlace:
                        case CaptureKind.MultiBranchRef:
                            return new MirPlace(cap.PrimaryLocalId, new MirProjection[] { new MirProjection.Deref(cap.MirType) });
                        case CaptureKind.ValueTemp when _bannedLinearUnionSwitchScrutineeRead.ContainsKey(fcr.Id):
                            return ReadBannedLinearUnionSwitchScrutinee(fcr.Id, cap.PrimaryLocalId, cap.MirType, span);
                        case CaptureKind.ValueTemp:
                            return MirPlace.Local(cap.PrimaryLocalId);
                    }
                }
                int dummy = CreateTempLocal(MapMirType(SubstituteType(fcr.Type)), span);
                return MirPlace.Local(dummy);
            }

            case IFieldReferenceOperation fr:
            {
                if (_thisLocalId is null &&
                    fr.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } &&
                    (_ctorFieldLocals.TryGetValue(fr.Field, out int ctorFid) || _ctorFieldLocals.TryGetValue(fr.Field.OriginalDefinition, out ctorFid)))
                {
                    return MirPlace.Local(ctorFid);
                }

                if (fr.Instance is null)
                {
                    EmitDiag(VibraniumDiagnosticIds.Unsupported, $"Static mutable field '{fr.Field.Name}' is banned in Vibranium.", span);
                    int dummy = CreateTempLocal(MapMirType(SubstituteType(fr.Type)), span);
                    return MirPlace.Local(dummy);
                }

                var baseType = SubstituteType(fr.Instance.Type);
                var basePlace = LowerPlaceOrMaterializeTemp(fr.Instance, MapMirType(baseType), isMutableAccess, span, isTwoPhaseBorrow);
                bool throughHeap = baseType?.TypeKind == TypeKind.Class;
                var fieldMirType = MapMirType(SubstituteType(fr.Type));
                int fieldIndex = GetFieldDeclarationIndex(fr.Field);

                var projs = new List<MirProjection>(basePlace.Projections.Count + 1);
                projs.AddRange(basePlace.Projections);
                projs.Add(new MirProjection.Field(fr.Field.Name, fieldIndex, fieldMirType, throughHeap, fr.Field.IsReadOnly));
                return new MirPlace(basePlace.LocalId, projs);
            }

            case IPropertyReferenceOperation prop when prop.Property.IsIndexer || prop.Property.Name == "Current" || prop.Property.ReturnsByRef || prop.Property.ReturnsByRefReadonly:
            {
                var elemT = MapMirType(SubstituteType(prop.Type));
                bool refMut = prop.Property.ReturnsByRef && !prop.Property.ReturnsByRefReadonly
                    ? (isMutableAccess || !prop.Property.IsIndexer)
                    : (prop.Property.ReturnsByRefReadonly ? false : isMutableAccess);
                int refTmp = CreateTempRefLocal(elemT, refMut, span);
                if (TryEmitRefProducingOperationInto(prop, refTmp, refMut, span, isTwoPhaseBorrow))
                {
                    return new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                }
                break;
            }

            case IImplicitIndexerReferenceOperation implIdx:
            {
                var elemT = MapMirType(SubstituteType(implIdx.Type));
                // N128: `ros[^1]` is a ref readonly element, as `ros[0]` is (the property case above).
                bool refMut = isMutableAccess && implIdx.IndexerSymbol is not IPropertySymbol { ReturnsByRefReadonly: true };
                int refTmp = CreateTempRefLocal(elemT, refMut, span);
                if (TryEmitRefProducingOperationInto(implIdx, refTmp, refMut, span, isTwoPhaseBorrow))
                {
                    return new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                }
                break;
            }

            case IInvocationOperation inv when inv.TargetMethod.ReturnsByRef || inv.TargetMethod.ReturnsByRefReadonly:
            {
                var elemT = MapMirType(SubstituteType(inv.Type));
                bool refMut = inv.TargetMethod.ReturnsByRef && !inv.TargetMethod.ReturnsByRefReadonly;
                int refTmp = CreateTempRefLocal(elemT, refMut, span);
                if (TryEmitRefProducingOperationInto(inv, refTmp, refMut, span, isTwoPhaseBorrow))
                {
                    return new MirPlace(refTmp, new MirProjection[] { new MirProjection.Deref(elemT) });
                }
                break;
            }
        }

        var fallbackType = MapMirType(SubstituteType(op.Type));
        int tempLoc = CreateTempLocal(fallbackType, span);
        var rv = LowerRvalue(op, fallbackType);
        _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(tempLoc), rv, span));
        return MirPlace.Local(tempLoc);
    }

    private IOperation UnwrapCapturedSingleOperation(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        while (op is IFlowCaptureReferenceOperation fcr &&
               _captures.TryGetValue(fcr.Id, out var cap) &&
               cap.SingleValueOp is { } sv)
        {
            op = UnwrapImplicitIdentityConversion(sv);
        }
        return op;
    }

    private static bool IsScalarCastableMirType(MirType type) =>
        type is MirType.Int8Type or
                MirType.UInt8Type or
                MirType.Int16Type or
                MirType.UInt16Type or
                MirType.CharType or
                MirType.Int32Type or
                MirType.UInt32Type or
                MirType.Int64Type or
                MirType.UInt64Type or
                MirType.NIntType or
                MirType.NUIntType or
                MirType.Float32Type or
                MirType.Float64Type or
                MirType.BoolType or
                MirType.EnumType;

    private void LowerSetterPropertyOrIndexerFlowCapture(CaptureInfo info, VibraniumSpan span)
    {
        var unwrapped = UnwrapImplicitIdentityConversion(info.SingleValueOp!);

        if (unwrapped is IPropertyReferenceOperation
            {
                Property:
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    SetMethod: { } propSetter,
                } prop,
            } propRef &&
            propSetter.ContainingType is INamedTypeSymbol propOwner)
        {
            var substSetter = SubstituteMethod(propSetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(propOwner);
            info.SubstSetter = substSetter;

            IMethodSymbol? substGetter = null;
            if (info.NeedsOldValueRead && prop.GetMethod is { } propGetter)
            {
                substGetter = SubstituteMethod(propGetter);
                _enqueueMethodInstantiation(substGetter);
                info.SubstGetter = substGetter;
            }

            if (!substSetter.IsStatic && propRef.Instance is not null)
            {
                var getRecvPass = substGetter is not null ? VibraniumSymbolHelpers.GetReceiverPassKind(substGetter) : (MirPassKind?)null;
                var setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
                bool needMutRecv = getRecvPass == MirPassKind.ImplicitThisMut || setRecvPass == MirPassKind.ImplicitThisMut;
                var recvMirT = MapMirType(SubstituteType(propRef.Instance.Type));
                var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(propRef.Instance.Syntax);
                var wrapperRead = !info.NeedsOldValueRead &&
                    ReadsReceiverInPlace(substSetter, propRef.Instance, substSetter.Parameters.Length)
                    ? BeginWrapperRead(propRef.Instance) : null;
                var recvPlace = needMutRecv
                    ? LowerPlace(propRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                    : LowerPlaceOrMaterializeTemp(propRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
                EndWrapperRead(wrapperRead);
                recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

                info.GetRecvPass = getRecvPass;
                info.SetRecvPass = setRecvPass;
                info.CapturedRecvPlace = recvPlace;
                info.CapturedRecvSpan = recvSpan;
                info.Hold = wrapperRead;
            }

            var idxParams = substSetter.Parameters.Take(substSetter.Parameters.Length - 1).ToImmutableArray();
            var evalIdxArgs = propRef.Arguments.Length > 0
                ? OrderCallArguments(propRef.Arguments, idxParams, span, materializeByValueTemps: true)
                : new List<MirCallArg>();
            info.CapturedIndexArgs = evalIdxArgs;

            if (info.NeedsOldValueRead && substGetter is not null)
            {
                var getArgs = new List<MirCallArg>(evalIdxArgs.Count + 1);
                if (info.GetRecvPass is { } getPass && info.CapturedRecvPlace is { } getRecv)
                {
                    getArgs.Add(new MirCallArg(getPass, null, getRecv, info.CapturedRecvSpan));
                }
                AddGetterIndexArgs(getArgs, evalIdxArgs);
                _currentStmts.Add(new MirStatement.Call(
                    MirPlace.Local(info.PrimaryLocalId),
                    null,
                    false,
                    VibraniumSymbolHelpers.GetMethodKey(substGetter),
                    getArgs,
                    span));
            }
            return;
        }

        if (unwrapped is IImplicitIndexerReferenceOperation
            {
                IndexerSymbol: IPropertySymbol
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    SetMethod: { } implSetter,
                } implProp,
                LengthSymbol: { } implLenSym,
            } implRef &&
            implSetter.ContainingType is INamedTypeSymbol implOwner)
        {
            var substSetter = SubstituteMethod(implSetter);
            _enqueueMethodInstantiation(substSetter);
            _enqueueTypeInstantiation(implOwner);
            info.SubstSetter = substSetter;

            IMethodSymbol? substGetter = null;
            if (info.NeedsOldValueRead && implProp.GetMethod is { } implGetter)
            {
                substGetter = SubstituteMethod(implGetter);
                _enqueueMethodInstantiation(substGetter);
                info.SubstGetter = substGetter;
            }

            var getRecvPass = substGetter is not null ? VibraniumSymbolHelpers.GetReceiverPassKind(substGetter) : (MirPassKind?)null;
            var setRecvPass = VibraniumSymbolHelpers.GetReceiverPassKind(substSetter);
            bool needMutRecv =
                getRecvPass == MirPassKind.ImplicitThisMut ||
                setRecvPass == MirPassKind.ImplicitThisMut ||
                (IsFromEndIndexOperation(implRef.Argument) && IsLengthSymbolMutating(implLenSym));

            var recvMirT = MapMirType(SubstituteType(implRef.Instance.Type));
            var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(implRef.Instance.Syntax);
            var recvPlace = needMutRecv
                ? LowerPlace(implRef.Instance, isMutableAccess: true, isTwoPhaseBorrow: true)
                : LowerPlaceOrMaterializeTemp(implRef.Instance, recvMirT, isMutableAccess: false, recvSpan);
            recvPlace = ReserveReceiverPlaceBeforeArgs(recvPlace, recvMirT, isMutable: needMutRecv, isTwoPhaseBorrow: needMutRecv, recvSpan);

            info.GetRecvPass = getRecvPass;
            info.SetRecvPass = setRecvPass;
            info.CapturedRecvPlace = recvPlace;
            info.CapturedRecvSpan = recvSpan;

            var idxOp = SnapshotCopyOperandIfNeeded(
                LowerSystemIndexGeneric(implRef.Argument, recvPlace, implLenSym, span),
                MirType.Int32,
                span);
            info.CapturedIndexArgs = new List<MirCallArg>(1)
            {
                new(MirPassKind.ByValue, idxOp, null, span),
            };

            if (info.NeedsOldValueRead && substGetter is not null)
            {
                var getArgs = new List<MirCallArg>(2)
                {
                    new(getRecvPass!.Value, null, recvPlace, recvSpan),
                    new(MirPassKind.ByValue, idxOp, null, span),
                };
                _currentStmts.Add(new MirStatement.Call(
                    MirPlace.Local(info.PrimaryLocalId),
                    null,
                    false,
                    VibraniumSymbolHelpers.GetMethodKey(substGetter),
                    getArgs,
                    span));
            }
        }
    }

    private bool IsCapturedPrimaryConstructorParameter(IParameterSymbol parameter)
    {
        if (_currentOwnerType is null)
        {
            return false;
        }
        foreach (var cp in VibraniumSymbolHelpers.GetCapturedPrimaryConstructorParameters(_currentOwnerType, _compilation))
        {
            if (SymbolEqualityComparer.Default.Equals(cp.OriginalDefinition, parameter.OriginalDefinition))
            {
                return true;
            }
        }
        return false;
    }

    private bool TryGetCapturedPrimaryConstructorFieldProjection(
        IParameterSymbol parameter,
        out MirProjection.Field projection)
    {
        projection = null!;
        if (_currentOwnerType is null)
        {
            return false;
        }
        var fields = VibraniumSymbolHelpers.GetInstanceFields(_currentOwnerType, _compilation);
        for (int i = 0; i < fields.Count; i++)
        {
            var desc = fields[i];
            if (desc.CapturedParameter is not null &&
                SymbolEqualityComparer.Default.Equals(desc.CapturedParameter.OriginalDefinition, parameter.OriginalDefinition))
            {
                bool throughHeap = _currentOwnerType.TypeKind == TypeKind.Class;
                var fieldMirType = MapMirType(SubstituteType(desc.Type));
                projection = new MirProjection.Field(desc.Name, i, fieldMirType, throughHeap, desc.IsReadOnly);
                return true;
            }
        }
        return false;
    }

    private int GetFieldDeclarationIndex(IFieldSymbol field)
    {
        var origOwner = field.ContainingType;
        if (origOwner is null) return 0;
        var fields = VibraniumSymbolHelpers.GetInstanceFields(origOwner, _compilation);
        for (int i = 0; i < fields.Count; i++)
        {
            if (fields[i].ExplicitField is { } ef &&
                SymbolEqualityComparer.Default.Equals(ef.OriginalDefinition, field.OriginalDefinition))
            {
                return i;
            }
        }
        return 0;
    }

    private List<MirCallArg> OrderCallArgumentsWithReducedReceiver(
        IOperation reducedReceiver,
        ImmutableArray<IArgumentOperation> args,
        ImmutableArray<IParameterSymbol> parameters,
        VibraniumSpan callSpan)
    {
        var p0 = parameters[0];
        var p0Type = MapMirType(SubstituteType(p0.Type));
        var p0Pass = VibraniumSymbolHelpers.GetParamPassKind(p0);
        var recvSpan = VibraniumSymbolHelpers.ToVibraniumSpan(reducedReceiver.Syntax);
        bool hasMoreArgs = args.Any(static a => a.ArgumentKind != ArgumentKind.DefaultValue);

        MirCallArg firstArg = LowerSingleCallArgumentValueOrPlace(
            reducedReceiver,
            p0Type,
            p0Pass,
            recvSpan,
            hasSubsequentExplicitArgs: hasMoreArgs,
            forceMaterializeByValue: hasMoreArgs);

        var remainingParams = parameters.RemoveAt(0);
        var tailArgs = OrderCallArguments(args, remainingParams, callSpan);
        var combined = new List<MirCallArg>(1 + tailArgs.Count) { firstArg };
        combined.AddRange(tailArgs);
        return combined;
    }

    private MirCallArg LowerSingleCallArgumentValueOrPlace(
        IOperation valueExpr,
        MirType pType,
        MirPassKind passKind,
        VibraniumSpan argSpan,
        bool hasSubsequentExplicitArgs,
        bool forceMaterializeByValue)
    {
        if (passKind == MirPassKind.ByValue)
        {
            var valOp = LowerOperand(valueExpr, pType);
            if (valOp is MirOperand.CopyOrMove cm)
            {
                bool isAlreadyCompilerTemp =
                    cm.Place.Projections.Count == 0 &&
                    cm.Place.LocalId >= 0 &&
                    cm.Place.LocalId < _locals.Count &&
                    _locals[cm.Place.LocalId].IsCompilerTemp;
                bool needsCapture = !_module.IsCopy(pType) || _module.IsViewLike(pType);
                if ((!isAlreadyCompilerTemp && hasSubsequentExplicitArgs && needsCapture) ||
                    (forceMaterializeByValue && !isAlreadyCompilerTemp))
                {
                    int tmp = CreateTempLocal(pType, argSpan);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(tmp),
                        new MirRvalue.Use(valOp, IsCallOperandReborrow: pType is MirType.SpanType or MirType.Utf8ScanViewType), argSpan));
                    valOp = new MirOperand.CopyOrMove(MirPlace.Local(tmp), pType, argSpan);
                }
            }
            return new MirCallArg(MirPassKind.ByValue, valOp, null, argSpan);
        }

        MirPlace borrowPlace;
        CallHold? hold = null;
        if (passKind == MirPassKind.Out && valueExpr is IDeclarationExpressionOperation { Expression: ILocalReferenceOperation outLr })
        {
            int outId = GetOrCreateLocalId(outLr.Local, isPatternBindingOverride: null);
            borrowPlace = MirPlace.Local(outId);
        }
        else if (passKind == MirPassKind.Out && valueExpr is IDiscardOperation)
        {
            int discId = CreateTempLocal(pType, argSpan, "__out_discard");
            borrowPlace = MirPlace.Local(discId);
        }
        else if (passKind == MirPassKind.In)
        {
            hold = BeginWrapperRead(valueExpr);
            borrowPlace = LowerPlaceOrMaterializeTemp(valueExpr, pType, isMutableAccess: false, argSpan);
            EndWrapperRead(hold);
            if (hasSubsequentExplicitArgs)
            {
                borrowPlace = ReserveReceiverPlaceBeforeArgs(borrowPlace, pType, isMutable: false, isTwoPhaseBorrow: false, argSpan);
            }
        }
        else
        {
            if (UnwrapCapturedSingleOperation(valueExpr) is IParameterReferenceOperation argPr &&
                IsCapturedPrimaryConstructorParameter(argPr.Parameter))
            {
                EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot pass captured primary-constructor parameter '{argPr.Parameter.Name}' by '{(passKind == MirPassKind.Out ? "out" : "ref")}'.", argSpan);
            }
            // N116: the hold an `out` target capture took before a branching later argument ends here, where the
            // argument's own borrow takes over.
            var outHold = BeginWrapperRead(valueExpr);
            borrowPlace = LowerPlace(valueExpr, isMutableAccess: true, isTwoPhaseBorrow: true);
            EndWrapperRead(outHold);
            KeepCallHold(outHold, argSpan);
        }
        // N116: an `out` target is borrowed exclusively until the call returns, so later arguments cannot use it.
        if (hasSubsequentExplicitArgs && passKind is MirPassKind.Ref or MirPassKind.Out)
        {
            borrowPlace = ReserveReceiverPlaceBeforeArgs(borrowPlace, pType, isMutable: true,
                isTwoPhaseBorrow: passKind == MirPassKind.Ref, argSpan, isOut: passKind == MirPassKind.Out);
        }
        return WithCallHold(new MirCallArg(passKind, null, borrowPlace, argSpan), hold);
    }

    private List<MirCallArg> OrderCallArguments(
        ImmutableArray<IArgumentOperation> args,
        ImmutableArray<IParameterSymbol> parameters,
        VibraniumSpan callSpan,
        bool materializeByValueTemps = false)
    {
        // Evaluate explicit source arguments strictly in syntactic source order (with reduced extension receiver first!),
        // then reorder the evaluated MirCallArgs into parameter-ordinal ABI order!
        IArgumentOperation? reducedExtensionReceiverArg = null;
        var explicitArgs = new List<IArgumentOperation>(args.Length);
        var defaultByOrdinal = new IArgumentOperation?[parameters.Length];

        foreach (var arg in args)
        {
            if (arg.Parameter is null || arg.Parameter.Ordinal >= parameters.Length)
            {
                continue;
            }
            if (arg.ArgumentKind == ArgumentKind.DefaultValue)
            {
                defaultByOrdinal[arg.Parameter.Ordinal] = arg;
            }
            else if (arg.IsImplicit && arg.Parameter.Ordinal == 0 && arg.Parameter.IsThis)
            {
                reducedExtensionReceiverArg = arg;
            }
            else
            {
                explicitArgs.Add(arg);
            }
        }

        // IsImplicit does not imply a default argument: Roslyn also synthesizes
        // argument operations for explicit assignment expressions. Only
        // ArgumentKind.DefaultValue may be postponed past source operands.
        explicitArgs.Sort(static (a, b) => a.Syntax.SpanStart.CompareTo(b.Syntax.SpanStart));
        if (reducedExtensionReceiverArg is not null)
        {
            explicitArgs.Insert(0, reducedExtensionReceiverArg);
        }
        CheckOutDeclarationsUnusedByLaterArgs(explicitArgs);

        bool hasOutOfOrderNamedArgs = false;
        for (int idx = 1; idx < explicitArgs.Count; idx++)
        {
            if (explicitArgs[idx].Parameter!.Ordinal < explicitArgs[idx - 1].Parameter!.Ordinal)
            {
                hasOutOfOrderNamedArgs = true;
                break;
            }
        }

        var evaluatedByOrdinal = new MirCallArg?[parameters.Length];

        for (int argIdx = 0; argIdx < explicitArgs.Count; argIdx++)
        {
            var argOp = explicitArgs[argIdx];
            bool hasSubsequentExplicitArgs = argIdx < explicitArgs.Count - 1;
            int ord = argOp.Parameter!.Ordinal;
            var param = parameters[ord];
            var pType = MapMirType(SubstituteType(param.Type));
            var passKind = VibraniumSymbolHelpers.GetParamPassKind(param);
            var argSpan = argOp == reducedExtensionReceiverArg
                ? VibraniumSymbolHelpers.ToVibraniumSpan(argOp.Value.Syntax)
                : VibraniumSymbolHelpers.ToVibraniumSpan(argOp.Syntax);

            evaluatedByOrdinal[ord] = LowerSingleCallArgumentValueOrPlace(
                argOp.Value,
                pType,
                passKind,
                argSpan,
                hasSubsequentExplicitArgs,
                forceMaterializeByValue: materializeByValueTemps || hasOutOfOrderNamedArgs);
        }

        var result = new List<MirCallArg>(parameters.Length);
        for (int i = 0; i < parameters.Length; i++)
        {
            if (evaluatedByOrdinal[i] is { } evaluated)
            {
                result.Add(evaluated);
                continue;
            }

            var param = parameters[i];
            var pType = MapMirType(SubstituteType(param.Type));
            var passKind = VibraniumSymbolHelpers.GetParamPassKind(param);
            var defArgOp = defaultByOrdinal[i];
            var argSpan = defArgOp is not null ? VibraniumSymbolHelpers.ToVibraniumSpan(defArgOp.Syntax) : callSpan;

            if (passKind == MirPassKind.ByValue)
            {
                MirOperand valOp;
                if (defArgOp is not null)
                {
                    valOp = LowerOperand(defArgOp.Value, pType);
                }
                else if (param.HasExplicitDefaultValue &&
                         TryMakeConstantOperand(param.ExplicitDefaultValue, SubstituteType(param.Type), pType, argSpan, out var defConst))
                {
                    valOp = defConst;
                }
                else
                {
                    valOp = new MirOperand.Const(new MirConstant.ZeroDefault(pType), pType, argSpan);
                }
                result.Add(new MirCallArg(MirPassKind.ByValue, valOp, null, argSpan));
            }
            else
            {
                var borrowPlace = MirPlace.Local(CreateTempLocal(pType, argSpan));
                result.Add(new MirCallArg(passKind, null, borrowPlace, argSpan));
            }
        }

        return result;
    }

    // N116: an `out var` declared in an argument is borrowed until the call returns, so a later argument may not name it.
    // Roslyn evaluates the declaration after a branching later argument, so no MIR borrow spans that argument.
    private void CheckOutDeclarationsUnusedByLaterArgs(List<IArgumentOperation> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Parameter?.RefKind != RefKind.Out ||
                args[i].Value is not IDeclarationExpressionOperation { Expression: ILocalReferenceOperation { Local: var declared } })
            {
                continue;
            }
            for (int j = i + 1; j < args.Count; j++)
            {
                // A direct ref/out/in argument of the same call (`F(out int y, out y)`) is an overlapping
                // call borrow (VIB002, N68), already reported by CheckOverlappingCallBorrows.
                if (args[j].Parameter?.RefKind is RefKind.Ref or RefKind.Out or RefKind.In &&
                    UnwrapImplicitIdentityConversion(args[j].Value) is ILocalReferenceOperation { Local: var directArgLocal } &&
                    SymbolEqualityComparer.Default.Equals(directArgLocal, declared))
                {
                    continue;
                }
                foreach (var use in args[j].Syntax.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    if (use.Identifier.ValueText == declared.Name &&
                        SymbolEqualityComparer.Default.Equals(_compilation.GetSemanticModel(use.SyntaxTree).GetSymbolInfo(use).Symbol, declared))
                    {
                        EmitDiag(VibraniumDiagnosticIds.ConflictWithActiveBorrow,
                            $"Cannot use '{declared.Name}' while the 'out' argument that declares it is borrowed for the call.",
                            VibraniumSymbolHelpers.ToVibraniumSpan(use));
                    }
                }
            }
        }
    }

    private bool TryMakeConstantOperand(
        object? rawVal,
        ITypeSymbol? opType,
        MirType fallbackMirType,
        VibraniumSpan span,
        out MirOperand operand)
    {
        operand = null!;
        if (rawVal is null)
        {
            return false;
        }

        if (opType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            // MIR represents only int-backed enums. Roslyn can still expose constants of an
            // unsupported enum type while the Analyzer walks a rejected program (even when
            // the enclosing C# expression has overflowed and has no constant value itself).
            var enumMir = new MirType.EnumType(VibraniumSymbolHelpers.GetTypeKey(enumType));
            if (enumType.EnumUnderlyingType?.SpecialType != SpecialType.System_Int32 || rawVal is not int enumInt)
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "Only int-backed enum constants are supported in Vibranium.", span);
                // Keep an unsupported constant from falling through to a read of an uninitialized place.
                operand = new MirOperand.Const(new MirConstant.ZeroDefault(enumMir), enumMir, span);
                return true;
            }

            operand = new MirOperand.Const(new MirConstant.Int32Const(enumInt), enumMir, span);
            return true;
        }

        MirType targetIntType = opType is not null ? MapMirType(opType) : fallbackMirType;

        switch (rawVal)
        {
            case bool b:
                operand = new MirOperand.Const(new MirConstant.BoolConst(b), MirType.Bool, span);
                return true;
            case byte by:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.UInt8;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(by), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(by), t, span);
                return true;
            }
            case sbyte sb:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.Int8;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(sb), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(sb), t, span);
                return true;
            }
            case short s:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.Int16;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(s), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(s), t, span);
                return true;
            }
            case ushort us:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.UInt16;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(us), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(us), t, span);
                return true;
            }
            case char ch:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.Char;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(ch), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(ch), t, span);
                return true;
            }
            case int i:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.Int32;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(i), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(i), t, span);
                return true;
            }
            case uint ui:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.UInt32;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(ui), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(unchecked((int)ui)), t, span);
                return true;
            }
            case long l:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.Int64;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(l), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(unchecked((int)l)), t, span);
                return true;
            }
            case ulong ul:
            {
                var t = MirType.IsInteger(targetIntType) ? targetIntType : MirType.UInt64;
                operand = MirType.GetIntegerBitWidth(t) == 64
                    ? new MirOperand.Const(new MirConstant.Int64Const(unchecked((long)ul)), t, span)
                    : new MirOperand.Const(new MirConstant.Int32Const(unchecked((int)ul)), t, span);
                return true;
            }
            case float f:
                operand = new MirOperand.Const(new MirConstant.Float32Const(f), MirType.Float32, span);
                return true;
            case double d:
                operand = new MirOperand.Const(new MirConstant.Float64Const(d), MirType.Float64, span);
                return true;
            case string str:
                operand = new MirOperand.Const(new MirConstant.StringConst(str), MirType.StringLiteral, span);
                return true;
            default:
                return false;
        }
    }

    private static MirBinaryOp MapBinaryOp(BinaryOperatorKind kind) =>
        kind switch
        {
            BinaryOperatorKind.Add => MirBinaryOp.Add,
            BinaryOperatorKind.Subtract => MirBinaryOp.Sub,
            BinaryOperatorKind.Multiply => MirBinaryOp.Mul,
            BinaryOperatorKind.Divide => MirBinaryOp.Div,
            BinaryOperatorKind.Remainder => MirBinaryOp.Rem,
            BinaryOperatorKind.And or BinaryOperatorKind.ConditionalAnd => MirBinaryOp.BitAnd,
            BinaryOperatorKind.Or or BinaryOperatorKind.ConditionalOr => MirBinaryOp.BitOr,
            BinaryOperatorKind.ExclusiveOr => MirBinaryOp.BitXor,
            BinaryOperatorKind.LeftShift => MirBinaryOp.Shl,
            BinaryOperatorKind.RightShift => MirBinaryOp.Shr,
            BinaryOperatorKind.UnsignedRightShift => MirBinaryOp.UShr,
            BinaryOperatorKind.Equals => MirBinaryOp.Eq,
            BinaryOperatorKind.NotEquals => MirBinaryOp.Ne,
            BinaryOperatorKind.LessThan => MirBinaryOp.Lt,
            BinaryOperatorKind.LessThanOrEqual => MirBinaryOp.Le,
            BinaryOperatorKind.GreaterThan => MirBinaryOp.Gt,
            BinaryOperatorKind.GreaterThanOrEqual => MirBinaryOp.Ge,
            _ => MirBinaryOp.Add,
        };

    private int GetOrCreateLocalId(ILocalSymbol sym, bool? isPatternBindingOverride)
    {
        if (_localMap.TryGetValue(sym, out int existing))
        {
            if (isPatternBindingOverride == true && !_locals[existing].IsPatternBinding)
            {
                _locals[existing] = _locals[existing] with { IsPatternBinding = true };
            }
            return existing;
        }

        var span = sym.Locations.FirstOrDefault(l => l.IsInSource) is { } loc
            ? VibraniumSymbolHelpers.ToVibraniumSpan(loc)
            : new VibraniumSpan(string.Empty, 0, 0, 1, 1);

        var valType = MapMirType(SubstituteType(sym.Type));
        bool isRef = sym.IsRef;
        bool isRefMut = isRef && sym.RefKind != RefKind.RefReadOnly;
        MirType storedType = isRef ? new MirType.RefType(valType, isRefMut) : valType;

        bool isPattern = isPatternBindingOverride ?? (sym.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is SingleVariableDesignationSyntax &&
            sym.DeclaringSyntaxReferences[0].GetSyntax().Parent is DeclarationPatternSyntax or VarPatternSyntax);

        int id = AddLocal(new MirLocal(
            _locals.Count,
            string.IsNullOrEmpty(sym.Name) ? $"__loc{_locals.Count}" : sym.Name,
            storedType,
            IsParam: false,
            PassKind: MirPassKind.ByValue,
            IsScoped: sym.ScopedKind != ScopedKind.None,
            IsUsingVar: sym.IsUsing,
            IsRefLocal: isRef,
            IsRefMutable: isRefMut,
            IsPatternBinding: isPattern,
            IsCompilerTemp: sym.IsImplicitlyDeclared,
            span));

        _localMap[sym] = id;
        return id;
    }

    private int CreatePatternLocal(MirType type, VibraniumSpan span, string prefix) =>
        AddLocal(new MirLocal(
            _locals.Count,
            $"{prefix}_{_tempCounter++}",
            type,
            IsParam: false,
            PassKind: MirPassKind.ByValue,
            IsScoped: false,
            IsUsingVar: false,
            IsRefLocal: false,
            IsRefMutable: false,
            IsPatternBinding: true,
            IsCompilerTemp: true,
            span));

    private MirOperand EmitInvalidGuardFallbackResult(
        ImmutableArray<IArgumentOperation> arguments,
        MirPlace? destPlace,
        VibraniumSpan span)
    {
        foreach (var arg in arguments)
        {
            ILocalSymbol? outSym = arg.Value switch
            {
                IDeclarationExpressionOperation { Expression: ILocalReferenceOperation declLr } => declLr.Local,
                ILocalReferenceOperation directLr => directLr.Local,
                _ => null,
            };
            if (outSym is not null)
            {
                int outId = GetOrCreateLocalId(outSym, isPatternBindingOverride: null);
                var outType = _locals[outId].Type;
                if (!_module.IsLinearResource(outType))
                {
                    _currentStmts.Add(new MirStatement.Assign(
                        MirPlace.Local(outId),
                        new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(outType), outType, span)),
                        span));
                }
            }
        }

        var falseOp = new MirOperand.Const(new MirConstant.BoolConst(false), MirType.Bool, span);
        if (destPlace is not null)
        {
            _currentStmts.Add(new MirStatement.Assign(destPlace, new MirRvalue.Use(falseOp), span));
            return new MirOperand.CopyOrMove(destPlace, MirType.Bool, span);
        }

        return falseOp;
    }

    private int CreateTempLocal(MirType type, VibraniumSpan span, string prefix = "__tmp") =>
        AddLocal(new MirLocal(
            _locals.Count,
            $"{prefix}_{_tempCounter++}",
            type,
            IsParam: false,
            PassKind: MirPassKind.ByValue,
            IsScoped: false,
            IsUsingVar: false,
            IsRefLocal: false,
            IsRefMutable: false,
            IsPatternBinding: false,
            IsCompilerTemp: true,
            span));

    private int CreateTempRefLocal(MirType targetType, bool isMutable, VibraniumSpan span, string prefix = "__ref", bool isSwitchMatchBorrow = false, bool isCallHold = false) =>
        AddLocal(new MirLocal(
            _locals.Count,
            $"{prefix}_{_tempCounter++}",
            new MirType.RefType(targetType, isMutable),
            IsParam: false,
            PassKind: MirPassKind.ByValue,
            IsScoped: false,
            IsUsingVar: false,
            IsRefLocal: true,
            IsRefMutable: isMutable,
            IsPatternBinding: false,
            IsCompilerTemp: true,
            span,
            IsSwitchMatchBorrow: isSwitchMatchBorrow,
            IsCallHold: isCallHold));

    private int AddLocal(MirLocal local)
    {
        int id = _locals.Count;
        _locals.Add(local);
        return id;
    }

    private MirType MapMirType(ITypeSymbol? type)
    {
        EnqueueConcreteTypesRecursive(type);
        return VibraniumSymbolHelpers.MapType(type, _module);
    }

    private void EnqueueConcreteTypesRecursive(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return;
        }
        if (named.TypeKind is TypeKind.Class or TypeKind.Struct &&
            IsInCompilationOrVibraniumStd(named.ContainingAssembly) &&
            !ContainsTypeParameters(named))
        {
            _enqueueTypeInstantiation(named);
        }
        foreach (var ta in named.TypeArguments)
        {
            EnqueueConcreteTypesRecursive(ta);
        }
    }

    private ITypeSymbol? SubstituteType(ITypeSymbol? type)
    {
        if (type is null || _typeMap.IsEmpty)
        {
            return type;
        }

        if (type is ITypeParameterSymbol tp && _typeMap.TryGetValue(tp.OriginalDefinition, out var mapped))
        {
            return mapped;
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var args = named.TypeArguments;
            var newArgs = new ITypeSymbol[args.Length];
            bool changed = false;
            for (int i = 0; i < args.Length; i++)
            {
                newArgs[i] = SubstituteType(args[i])!;
                if (!SymbolEqualityComparer.Default.Equals(newArgs[i], args[i]))
                {
                    changed = true;
                }
            }
            if (changed)
            {
                return named.OriginalDefinition.Construct(newArgs);
            }
        }

        return type;
    }

    private IMethodSymbol SubstituteMethod(IMethodSymbol method)
    {
        if (_typeMap.IsEmpty)
        {
            return method;
        }

        IMethodSymbol result = method;
        if (result.ContainingType is { IsGenericType: true } owner)
        {
            var substOwner = (INamedTypeSymbol)SubstituteType(owner)!;
            if (!SymbolEqualityComparer.Default.Equals(substOwner, owner))
            {
                foreach (var m in substOwner.GetMembers(result.Name).OfType<IMethodSymbol>())
                {
                    if (SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, result.OriginalDefinition))
                    {
                        result = m;
                        break;
                    }
                }
            }
        }

        if (result.IsGenericMethod)
        {
            var tArgs = result.TypeArguments;
            var newArgs = new ITypeSymbol[tArgs.Length];
            bool changed = false;
            for (int i = 0; i < tArgs.Length; i++)
            {
                newArgs[i] = SubstituteType(tArgs[i])!;
                if (!SymbolEqualityComparer.Default.Equals(newArgs[i], tArgs[i]))
                {
                    changed = true;
                }
            }
            if (changed)
            {
                result = result.ConstructedFrom.Construct(newArgs);
            }
        }

        return result;
    }

    private static bool ContainsTypeParameters(ITypeSymbol type)
    {
        if (type is ITypeParameterSymbol) return true;
        if (type is INamedTypeSymbol named)
        {
            foreach (var arg in named.TypeArguments)
            {
                if (ContainsTypeParameters(arg)) return true;
            }
        }
        return false;
    }

    private bool IsInCompilationOrVibraniumStd(IAssemblySymbol? asm) =>
        SymbolEqualityComparer.Default.Equals(asm, _compilation.Assembly) ||
        string.Equals(asm?.Name, "Vibranium.Std", StringComparison.Ordinal);

    private void EmitDiag(string id, string message, VibraniumSpan span)
    {
        _diagnostics.Add(new VibraniumDiagnostic(id, message, span));
    }
}
