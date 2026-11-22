using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Vibranium.Compiler.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class CfgMirLowerer(
    CSharpCompilation compilation,
    MirModule module,
    List<VibraniumDiagnostic> diagnostics,
    Action<IMethodSymbol> enqueueMethodInstantiation,
    Action<INamedTypeSymbol> enqueueTypeInstantiation,
    ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol> typeMap,
    SubsetDeclarationCuts declarationCuts)
{
    private static readonly ConditionalWeakTable<CSharpCompilation, ConditionalWeakTable<SyntaxTree, Lazy<HashSet<int>>>> s_exhaustiveStatementSwitches = new();

    private readonly CSharpCompilation _compilation = compilation;
    private readonly MirModule _module = module;
    private readonly List<VibraniumDiagnostic> _diagnostics = diagnostics;
    private readonly Action<IMethodSymbol> _enqueueMethodInstantiation = enqueueMethodInstantiation;
    private readonly Action<INamedTypeSymbol> _enqueueTypeInstantiation = enqueueTypeInstantiation;
    private readonly ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol> _typeMap = typeMap;
    private readonly SubsetDeclarationCuts _declarationCuts = declarationCuts;

    private readonly List<MirLocal> _locals = new();
    private readonly Dictionary<ILocalSymbol, int> _localMap = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IParameterSymbol, int> _paramMap = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, int> _ctorFieldLocals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IParameterSymbol, int> _ctorCapturedParamFieldLocals = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IFieldSymbol> _ctorNonZeroSafeFieldsRequiringInit = new(SymbolEqualityComparer.Default);
    private INamedTypeSymbol? _currentOwnerType;
    private int? _thisLocalId;
    private int _tempCounter;

    private enum CaptureKind
    {
        ValueTemp,
        UsingExistingPlace,
        DirectPlace,
        ReservedPlace,
        SetterPropertyOrIndexer,
        MultiBranchRef,
        SpanEnumerator,
        Utf8ScanEnumerator,
    }

    private sealed class CaptureInfo
    {
        public CaptureKind Kind;
        public int PrimaryLocalId = -1;
        public int SecondaryLocalId = -1;
        public int TertiaryLocalId = -1;
        public int QuaternaryLocalId = -1;
        public int ScanMode;
        public MirType MirType = MirType.Void;
        public bool IsMutable;
        public bool IsTwoPhaseBorrow;
        public IOperation? SingleValueOp;
        public IOperation? StrictConstantSource;
        public MirPlace? ResolvedDirectPlace;
        public IMethodSymbol? SubstGetter;
        public IMethodSymbol? SubstSetter;
        public MirPassKind? GetRecvPass;
        public MirPassKind? SetRecvPass;
        public MirPlace? CapturedRecvPlace;
        public VibraniumSpan CapturedRecvSpan;
        public List<MirCallArg>? CapturedIndexArgs;
        public bool NeedsOldValueRead;
        public int UpdateOldValueLocalId = -1;
        public int PatternUseCount;
        public bool HoldsMovedPlace;
        public int? MovedPlaceLocalId;
    }

    private readonly Dictionary<CaptureId, CaptureInfo> _captures = new();
    private readonly Dictionary<ControlFlowRegion, (int LocalId, ITypeSymbol LocalType)> _tryFinallyUsingLocal = new();
    private readonly Dictionary<int, List<MirStatement>> _mirBlockStmts = new();
    private readonly Dictionary<int, MirTerminator> _mirBlockTerms = new();
    private readonly Dictionary<int, List<MirStatement>> _pendingBlockEntryPrepends = new();
    private readonly HashSet<int> _collapsedCfgBlockOrdinals = new();
    private readonly Dictionary<SyntaxNode, bool> _bannedLinearUnionSwitches = new();
    private readonly Dictionary<CaptureId, bool> _bannedLinearUnionSwitchScrutineeRead = new();
    private readonly Dictionary<SyntaxNode, int> _switchMatchBorrows = new();
    private int _nextSyntheticBlockId;
    private int _currentBlockId;
    private List<int> _currentOrderedBlockIds = new();
    private List<MirStatement> _currentStmts = new();
    private MirFunctionSignature? _currentSignature;

    public MirFunction LowerRegularMethod(
        IMethodSymbol method,
        MirFunctionSignature signature,
        ControlFlowGraph cfg,
        bool isEntryPoint,
        VibraniumSpan fnSpan)
    {
        _currentSignature = signature;
        _currentOwnerType = method.ContainingType;
        SetupParameters(method, signature, fnSpan);
        PreScanRegionsAndCaptures(cfg);

        _nextSyntheticBlockId = cfg.Blocks.Length + 1;
        var orderedBlockIds = new List<int>();

        foreach (var block in cfg.Blocks)
        {
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion) || _collapsedCfgBlockOrdinals.Contains(block.Ordinal))
            {
                continue;
            }
            orderedBlockIds.Add(block.Ordinal);
        }

        foreach (var block in cfg.Blocks)
        {
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion))
            {
                continue;
            }

            _currentBlockId = block.Ordinal;
            _currentOrderedBlockIds = orderedBlockIds;
            _currentStmts = new List<MirStatement>();
            if (_pendingBlockEntryPrepends.TryGetValue(block.Ordinal, out var prepends))
            {
                _currentStmts.AddRange(prepends);
            }

            MirTerminator term;
            if (block.Kind == BasicBlockKind.Exit)
            {
                term = new MirTerminator.Return(null, null, fnSpan);
            }
            else
            {
                foreach (var op in block.Operations)
                {
                    LowerStatementOperation(op);
                    KeepSwitchMatchBorrows(op.Syntax);
                }
                term = LowerBlockTerminator(cfg, block, isConstructor: false, ctorOwnerType: null, fnSpan, orderedBlockIds);
                KeepSwitchMatchBorrows(block.BranchValue?.Syntax);
            }

            _mirBlockStmts[_currentBlockId] = _currentStmts;
            _mirBlockTerms[_currentBlockId] = term;
        }

        foreach (var kvp in _pendingBlockEntryPrepends)
        {
            if (_mirBlockStmts.TryGetValue(kvp.Key, out var stmts) && !stmts.Contains(kvp.Value[0]))
            {
                stmts.InsertRange(0, kvp.Value);
            }
        }

        var mirBlocks = new List<MirBasicBlock>(orderedBlockIds.Count);
        foreach (int bid in orderedBlockIds)
        {
            if (_mirBlockStmts.TryGetValue(bid, out var stmts) && _mirBlockTerms.TryGetValue(bid, out var term))
            {
                mirBlocks.Add(new MirBasicBlock(bid, stmts, term));
            }
        }

        return new MirFunction(signature, isEntryPoint, _locals, mirBlocks, fnSpan);
    }

    public MirFunction LowerConstructor(
        IMethodSymbol ctorMethod,
        INamedTypeSymbol ownerType,
        MirFunctionSignature signature,
        ControlFlowGraph? bodyCfg,
        VibraniumSpan fnSpan)
    {
        _currentSignature = signature;
        for (int i = 0; i < ctorMethod.Parameters.Length; i++)
        {
            var p = ctorMethod.Parameters[i];
            var pSig = signature.Params[i];
            var pSpan = p.Locations.FirstOrDefault(l => l.IsInSource) is { } loc
                ? VibraniumSymbolHelpers.ToVibraniumSpan(loc)
                : fnSpan;
            int id = AddLocal(new MirLocal(
                _locals.Count,
                p.Name,
                pSig.Type,
                IsParam: true,
                PassKind: pSig.PassKind,
                IsScoped: pSig.IsScoped,
                IsUsingVar: false,
                IsRefLocal: false,
                IsRefMutable: false,
                IsPatternBinding: false,
                IsCompilerTemp: false,
                pSpan));
            _paramMap[p.OriginalDefinition] = id;
            _paramMap[p] = id;
        }

        _currentOwnerType = ownerType;
        var allInstanceFields = VibraniumSymbolHelpers.GetInstanceFields(ownerType, _compilation);
        var fieldInitOps = new List<(IFieldSymbol Field, IOperation? InitOp, MirType FieldMirType)>();
        var capturedInitSpecs = new List<(IParameterSymbol Param, int FieldLocalId, MirType FieldMirType, VibraniumSpan Span)>();

        foreach (var desc in allInstanceFields)
        {
            var substFieldType = SubstituteType(desc.Type);
            var fieldMirType = MapMirType(substFieldType);
            int fid = AddLocal(new MirLocal(
                _locals.Count,
                $"__field_{desc.Name}",
                fieldMirType,
                IsParam: false,
                PassKind: MirPassKind.ByValue,
                IsScoped: false,
                IsUsingVar: false,
                IsRefLocal: false,
                IsRefMutable: false,
                IsPatternBinding: false,
                IsCompilerTemp: true,
                fnSpan));

            if (desc.ExplicitField is { } field)
            {
                _ctorFieldLocals[field.OriginalDefinition] = fid;
                _ctorFieldLocals[field] = fid;

                IOperation? initOp = TryGetFieldInitializerOperation(field);
                fieldInitOps.Add((field, initOp, fieldMirType));

                if (initOp is null && !VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(substFieldType, _compilation))
                {
                    _ctorNonZeroSafeFieldsRequiringInit.Add(field.OriginalDefinition);
                }
            }
            else if (desc.CapturedParameter is { } capParam)
            {
                _ctorCapturedParamFieldLocals[capParam.OriginalDefinition] = fid;
                _ctorCapturedParamFieldLocals[capParam] = fid;
                var cpSpan = capParam.Locations.FirstOrDefault(l => l.IsInSource) is { } cpLoc
                    ? VibraniumSymbolHelpers.ToVibraniumSpan(cpLoc)
                    : fnSpan;
                capturedInitSpecs.Add((capParam, fid, fieldMirType, cpSpan));
            }
        }

        if (bodyCfg is null)
        {
            _currentStmts = new List<MirStatement>();
            EmitFieldInitializers(fieldInitOps, fnSpan);
            EmitCapturedPrimaryCtorFieldInitializers(capturedInitSpecs, fnSpan);

            foreach (var uninitField in _ctorNonZeroSafeFieldsRequiringInit)
            {
                EmitDiag(
                    VibraniumDiagnosticIds.NullOrZeroUnsafe,
                    $"Constructor of '{ownerType.Name}' leaves non-zero-safe field '{uninitField.Name}' unassigned.",
                    fnSpan);
                int fid = _ctorFieldLocals[uninitField];
                var fType = _locals[fid].Type;
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(fid),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(fType), fType, fnSpan)),
                    fnSpan));
            }

            var term = EmitConstructorReturnSequence(ownerType, allInstanceFields, fnSpan);
            var block = new MirBasicBlock(0, _currentStmts, term);
            return new MirFunction(signature, false, _locals, new[] { block }, fnSpan);
        }

        PreScanRegionsAndCaptures(bodyCfg);
        _nextSyntheticBlockId = bodyCfg.Blocks.Length + 1;

        if (_ctorNonZeroSafeFieldsRequiringInit.Count > 0)
        {
            CheckConstructorFieldDefiniteAssignment(bodyCfg, ownerType, fnSpan);
        }

        var orderedBlockIds = new List<int>();
        foreach (var block in bodyCfg.Blocks)
        {
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion)) continue;
            orderedBlockIds.Add(block.Ordinal);
        }

        foreach (var block in bodyCfg.Blocks)
        {
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion) || _collapsedCfgBlockOrdinals.Contains(block.Ordinal)) continue;

            _currentBlockId = block.Ordinal;
            _currentOrderedBlockIds = orderedBlockIds;
            _currentStmts = new List<MirStatement>();
            if (block.Kind == BasicBlockKind.Entry)
            {
                EmitFieldInitializers(fieldInitOps, fnSpan);
                EmitCapturedPrimaryCtorFieldInitializers(capturedInitSpecs, fnSpan);
            }
            if (_pendingBlockEntryPrepends.TryGetValue(block.Ordinal, out var prepends))
            {
                _currentStmts.AddRange(prepends);
            }

            MirTerminator term;
            if (block.Kind == BasicBlockKind.Exit)
            {
                term = EmitConstructorReturnSequence(ownerType, allInstanceFields, fnSpan);
            }
            else
            {
                foreach (var op in block.Operations)
                {
                    LowerStatementOperation(op);
                    KeepSwitchMatchBorrows(op.Syntax);
                }
                term = LowerBlockTerminator(bodyCfg, block, isConstructor: true, ctorOwnerType: ownerType, fnSpan, orderedBlockIds);
                KeepSwitchMatchBorrows(block.BranchValue?.Syntax);
            }

            _mirBlockStmts[_currentBlockId] = _currentStmts;
            _mirBlockTerms[_currentBlockId] = term;
        }

        var mirBlocks = new List<MirBasicBlock>(orderedBlockIds.Count);
        foreach (int bid in orderedBlockIds)
        {
            if (_mirBlockStmts.TryGetValue(bid, out var stmts) && _mirBlockTerms.TryGetValue(bid, out var term))
            {
                mirBlocks.Add(new MirBasicBlock(bid, stmts, term));
            }
        }

        return new MirFunction(signature, false, _locals, mirBlocks, fnSpan);
    }

    private void CheckConstructorFieldDefiniteAssignment(ControlFlowGraph cfg, INamedTypeSymbol ownerType, VibraniumSpan ctorSpan)
    {
        var assignedAtEntry = new Dictionary<int, HashSet<IFieldSymbol>>();
        assignedAtEntry[0] = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);

        var queue = new Queue<int>();
        queue.Enqueue(0);

        while (queue.Count > 0)
        {
            int ord = queue.Dequeue();
            var block = cfg.Blocks[ord];
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion)) continue;

            var cur = new HashSet<IFieldSymbol>(assignedAtEntry[ord], SymbolEqualityComparer.Default);
            foreach (var op in block.Operations)
            {
                CollectAssignedCtorFields(op, cur);
            }
            if (block.BranchValue is not null)
            {
                CollectAssignedCtorFields(block.BranchValue, cur);
            }

            if (BlockCallsPanic(block))
            {
                continue;
            }

            var branches = new[] { block.FallThroughSuccessor, block.ConditionalSuccessor };
            if (block.BranchValue is { } condition && IsExhaustiveStatementSwitchFinalTest(condition))
            {
                // Use the same source-reachability correction as MIR lowering:
                // the public CFG can retain an impossible unmatched switch edge.
                branches = new[] { block.ConditionKind == ControlFlowConditionKind.WhenTrue
                    ? block.ConditionalSuccessor : block.FallThroughSuccessor };
            }
            foreach (var branch in branches)
            {
                if (branch?.Destination is null) continue;
                int destOrd = branch.Destination.Ordinal;
                if (!assignedAtEntry.TryGetValue(destOrd, out var destSet))
                {
                    assignedAtEntry[destOrd] = new HashSet<IFieldSymbol>(cur, SymbolEqualityComparer.Default);
                    queue.Enqueue(destOrd);
                }
                else
                {
                    int before = destSet.Count;
                    destSet.IntersectWith(cur);
                    if (destSet.Count != before)
                    {
                        queue.Enqueue(destOrd);
                    }
                }
            }
        }

        int exitOrd = cfg.Blocks.First(b => b.Kind == BasicBlockKind.Exit).Ordinal;
        if (!assignedAtEntry.TryGetValue(exitOrd, out var assignedAtExit))
        {
            return;
        }

        foreach (var reqField in _ctorNonZeroSafeFieldsRequiringInit)
        {
            if (!assignedAtExit.Contains(reqField))
            {
                EmitDiag(
                    VibraniumDiagnosticIds.NullOrZeroUnsafe,
                    $"Constructor of '{ownerType.Name}' must initialize non-zero-safe field '{reqField.Name}' on all control-flow paths.",
                    ctorSpan);

                int fid = _ctorFieldLocals[reqField];
                var fType = _locals[fid].Type;
                _pendingBlockEntryPrepends.TryGetValue(0, out var list);
                list ??= (_pendingBlockEntryPrepends[0] = new List<MirStatement>());
                list.Add(new MirStatement.Assign(
                    MirPlace.Local(fid),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(fType), fType, ctorSpan)),
                    ctorSpan));
            }
        }
    }

    private static bool BlockCallsPanic(BasicBlock block)
    {
        foreach (var op in block.Operations)
        {
            foreach (var sub in op.DescendantsAndSelf())
            {
                if (sub is IInvocationOperation inv &&
                    inv.TargetMethod.Name == "Panic" &&
                    inv.TargetMethod.ContainingType?.Name == "VibraniumRuntime")
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void CollectAssignedCtorFields(IOperation root, HashSet<IFieldSymbol> assigned)
    {
        IOperation UnwrapCapturedSingle(IOperation expr)
        {
            expr = UnwrapImplicitIdentityConversion(expr);
            while (expr is IFlowCaptureReferenceOperation fcr &&
                   _captures.TryGetValue(fcr.Id, out var cap) &&
                   cap.SingleValueOp is { } sv)
            {
                expr = UnwrapImplicitIdentityConversion(sv);
            }
            return expr;
        }

        foreach (var op in root.DescendantsAndSelf())
        {
            if (op is ISimpleAssignmentOperation { IsRef: false } sa &&
                UnwrapCapturedSingle(sa.Target) is IFieldReferenceOperation fr &&
                fr.Instance is not null &&
                UnwrapCapturedSingle(fr.Instance) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            {
                assigned.Add(fr.Field.OriginalDefinition);
            }
            else if (op is IArgumentOperation { Parameter.RefKind: RefKind.Out } outArg &&
                     UnwrapCapturedSingle(outArg.Value) is IFieldReferenceOperation outFr &&
                     outFr.Instance is not null &&
                     UnwrapCapturedSingle(outFr.Instance) is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            {
                assigned.Add(outFr.Field.OriginalDefinition);
            }
        }
    }

    private IOperation? TryGetFieldInitializerOperation(IFieldSymbol field)
    {
        var orig = field.OriginalDefinition;
        foreach (var syntaxRef in orig.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } initClause })
            {
                var model = _compilation.GetSemanticModel(initClause.SyntaxTree);
                if (model.GetOperation(initClause) is IFieldInitializerOperation fieldInitOp)
                {
                    return fieldInitOp.Value;
                }
                return model.GetOperation(initClause.Value);
            }
        }
        if (orig.AssociatedSymbol is IPropertySymbol assocProp)
        {
            foreach (var syntaxRef in assocProp.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax { Initializer: { } propInit })
                {
                    var model = _compilation.GetSemanticModel(propInit.SyntaxTree);
                    if (model.GetOperation(propInit) is IPropertyInitializerOperation propInitOp)
                    {
                        return propInitOp.Value;
                    }
                    return model.GetOperation(propInit.Value);
                }
            }
        }
        return null;
    }

    private void EmitFieldInitializers(
        List<(IFieldSymbol Field, IOperation? InitOp, MirType FieldMirType)> fieldInitOps,
        VibraniumSpan fallbackSpan)
    {
        foreach (var (field, initOp, fieldMirType) in fieldInitOps)
        {
            int fid = _ctorFieldLocals[field.OriginalDefinition];
            if (initOp is not null)
            {
                var span = VibraniumSymbolHelpers.ToVibraniumSpan(initOp.Syntax);
                var rv = LowerFieldInitializerRvalue(field, initOp, fieldMirType, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(fid), rv, span));
            }
            else if (VibraniumSymbolHelpers.IsZeroSafeTypeSymbol(SubstituteType(field.Type), _compilation))
            {
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(fid),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(fieldMirType), fieldMirType, fallbackSpan)),
                    fallbackSpan));
            }
        }
    }

    private void EmitCapturedPrimaryCtorFieldInitializers(
        List<(IParameterSymbol Param, int FieldLocalId, MirType FieldMirType, VibraniumSpan Span)> capturedInitSpecs,
        VibraniumSpan fallbackSpan)
    {
        foreach (var (capParam, fid, fieldMirType, cpSpan) in capturedInitSpecs)
        {
            if (_paramMap.TryGetValue(capParam, out int paramLocalId) ||
                _paramMap.TryGetValue(capParam.OriginalDefinition, out paramLocalId))
            {
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(fid),
                    new MirRvalue.Use(new MirOperand.CopyOrMove(MirPlace.Local(paramLocalId), fieldMirType, cpSpan)),
                    cpSpan));
            }
            else
            {
                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(fid),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(fieldMirType), fieldMirType, fallbackSpan)),
                    fallbackSpan));
            }
        }
    }

    private MirTerminator EmitConstructorReturnSequence(
        INamedTypeSymbol ownerType,
        IReadOnlyList<VibraniumSymbolHelpers.InstanceFieldDescriptor> instanceFields,
        VibraniumSpan span)
    {
        var fieldOperands = new List<MirOperand>(instanceFields.Count);
        foreach (var desc in instanceFields)
        {
            int fid = desc.ExplicitField is { } ef
                ? _ctorFieldLocals[ef.OriginalDefinition]
                : _ctorCapturedParamFieldLocals[desc.CapturedParameter!.OriginalDefinition];
            var fType = _locals[fid].Type;
            fieldOperands.Add(new MirOperand.CopyOrMove(MirPlace.Local(fid), fType, span));
        }

        var ownerMirType = MapMirType(ownerType);
        string typeKey = VibraniumSymbolHelpers.GetTypeKey(ownerType);
        MirRvalue allocRv = ownerType.TypeKind == TypeKind.Class
            ? new MirRvalue.AllocClass(typeKey, fieldOperands)
            : new MirRvalue.ConstructStruct(typeKey, fieldOperands);

        int resLocal = CreateTempLocal(ownerMirType, span);
        _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(resLocal), allocRv, span));
        return new MirTerminator.Return(new MirOperand.CopyOrMove(MirPlace.Local(resLocal), ownerMirType, span), null, span);
    }

    private void SetupParameters(IMethodSymbol method, MirFunctionSignature signature, VibraniumSpan fnSpan)
    {
        int sigIdx = 0;
        if (!method.IsStatic)
        {
            var thisSig = signature.Params[sigIdx++];
            int thisId = AddLocal(new MirLocal(
                _locals.Count,
                "this",
                thisSig.Type,
                IsParam: true,
                PassKind: thisSig.PassKind,
                IsScoped: thisSig.IsScoped,
                IsUsingVar: false,
                IsRefLocal: false,
                IsRefMutable: false,
                IsPatternBinding: false,
                IsCompilerTemp: false,
                fnSpan));
            _thisLocalId = thisId;
        }

        for (int i = 0; i < method.Parameters.Length; i++)
        {
            var p = method.Parameters[i];
            var pSig = signature.Params[sigIdx++];
            var pSpan = p.Locations.FirstOrDefault(l => l.IsInSource) is { } loc
                ? VibraniumSymbolHelpers.ToVibraniumSpan(loc)
                : fnSpan;
            int id = AddLocal(new MirLocal(
                _locals.Count,
                p.Name,
                pSig.Type,
                IsParam: true,
                PassKind: pSig.PassKind,
                IsScoped: pSig.IsScoped,
                IsUsingVar: false,
                IsRefLocal: false,
                IsRefMutable: false,
                IsPatternBinding: false,
                IsCompilerTemp: false,
                pSpan));
            _paramMap[p.OriginalDefinition] = id;
            _paramMap[p] = id;
        }
    }

    private void PreScanRegionsAndCaptures(ControlFlowGraph cfg)
    {
        RegisterRegionLocals(cfg.Root);
        MapTryFinallyUsingRegions(cfg, cfg.Root);

        var defsByCapture = new Dictionary<CaptureId, List<IFlowCaptureOperation>>();
        var allRefsByCapture = new Dictionary<CaptureId, IFlowCaptureReferenceOperation>();
        var patternUsesByCapture = new Dictionary<CaptureId, int>();
        var usedByRefBinding = new HashSet<CaptureId>();
        var refBindingMutable = new Dictionary<CaptureId, bool>();
        var initViaCaptureRef = new HashSet<CaptureId>();
        var enumeratorElementBoundMutably = new HashSet<CaptureId>();
        var usedAsPlaceOrLvalue = new HashSet<CaptureId>();
        var usedOutsideIsPatternAsPlace = new HashSet<CaptureId>();
        var usedMutablyAsPlace = new HashSet<CaptureId>();
        var usedAsDirectSimpleAssignTarget = new HashSet<CaptureId>();
        var usedAsCompoundAssignTarget = new HashSet<CaptureId>();
        var usedAsIncDecTarget = new HashSet<CaptureId>();
        var usedWithUserDefinedAddAssign = new HashSet<CaptureId>();
        var usedAsScalarCompoundOrIncDecTarget = new HashSet<CaptureId>();
        var usedAsOutArg = new HashSet<CaptureId>();
        var movedReceiverOfCall = new HashSet<CaptureId>();

        foreach (var block in cfg.Blocks)
        {
            if (IsInsideFinallyOrCatchRegion(block.EnclosingRegion)) continue;

            if (block.BranchValue is IFlowCaptureReferenceOperation retFcr && IsRefReturnBlock(block))
            {
                usedAsPlaceOrLvalue.Add(retFcr.Id);
                usedOutsideIsPatternAsPlace.Add(retFcr.Id);
                if (_currentSignature?.ReturnRefIsMutable == true)
                {
                    usedMutablyAsPlace.Add(retFcr.Id);
                }
            }

            foreach (var op in EnumerateBlockOperations(block))
            {
                foreach (var sub in op.DescendantsAndSelf())
                {
                    switch (sub)
                    {
                        case IFlowCaptureOperation fc:
                            if (!defsByCapture.TryGetValue(fc.Id, out var list))
                            {
                                list = new List<IFlowCaptureOperation>();
                                defsByCapture[fc.Id] = list;
                            }
                            list.Add(fc);
                            break;

                        case IFlowCaptureReferenceOperation fcr:
                            if (!allRefsByCapture.ContainsKey(fcr.Id)) allRefsByCapture[fcr.Id] = fcr;
                            if (fcr.IsInitialization)
                            {
                                initViaCaptureRef.Add(fcr.Id);
                                usedAsPlaceOrLvalue.Add(fcr.Id);
                            }
                            break;

                        case ISimpleAssignmentOperation { IsRef: true } refAssign:
                            if (UnwrapImplicitIdentityConversion(refAssign.Value) is IFlowCaptureReferenceOperation valFcr)
                            {
                                usedByRefBinding.Add(valFcr.Id);
                                usedAsPlaceOrLvalue.Add(valFcr.Id);
                                usedOutsideIsPatternAsPlace.Add(valFcr.Id);
                                bool isMut = refAssign.Target is not ILocalReferenceOperation lr || lr.Local.RefKind != RefKind.RefReadOnly;
                                if (isMut)
                                {
                                    refBindingMutable[valFcr.Id] = true;
                                    usedMutablyAsPlace.Add(valFcr.Id);
                                }
                                else if (!refBindingMutable.ContainsKey(valFcr.Id))
                                {
                                    refBindingMutable[valFcr.Id] = false;
                                }
                            }
                            else if (refAssign.Value is IPropertyReferenceOperation { Property.Name: "Current", Instance: IFlowCaptureReferenceOperation enumFcr })
                            {
                                bool isMut = refAssign.Target is not ILocalReferenceOperation lr || lr.Local.RefKind != RefKind.RefReadOnly;
                                if (isMut) enumeratorElementBoundMutably.Add(enumFcr.Id);
                            }
                            break;

                        case ISimpleAssignmentOperation sa when !sa.IsRef && UnwrapImplicitIdentityConversion(sa.Target) is IFlowCaptureReferenceOperation saFcr:
                            usedAsPlaceOrLvalue.Add(saFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(saFcr.Id);
                            usedMutablyAsPlace.Add(saFcr.Id);
                            usedAsDirectSimpleAssignTarget.Add(saFcr.Id);
                            break;

                        case ICompoundAssignmentOperation ca when UnwrapImplicitIdentityConversion(ca.Target) is IFlowCaptureReferenceOperation caFcr:
                            usedAsPlaceOrLvalue.Add(caFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(caFcr.Id);
                            if (ca.OperatorMethod is { } caOpMethod && VibraniumSymbolHelpers.IsSupportedCompoundAddAssignOperator(caOpMethod))
                            {
                                usedWithUserDefinedAddAssign.Add(caFcr.Id);
                                if (VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(caOpMethod)) == MirPassKind.ImplicitThisMut)
                                {
                                    usedMutablyAsPlace.Add(caFcr.Id);
                                }
                            }
                            else
                            {
                                usedMutablyAsPlace.Add(caFcr.Id);
                                if (ca.OperatorMethod is null &&
                                    !VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(ca.Target.Type), "Vec"))
                                {
                                    usedAsCompoundAssignTarget.Add(caFcr.Id);
                                    usedAsScalarCompoundOrIncDecTarget.Add(caFcr.Id);
                                }
                            }
                            break;

                        case IIncrementOrDecrementOperation idOp when UnwrapImplicitIdentityConversion(idOp.Target) is IFlowCaptureReferenceOperation idFcr:
                            usedAsPlaceOrLvalue.Add(idFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(idFcr.Id);
                            usedMutablyAsPlace.Add(idFcr.Id);
                            if (idOp.OperatorMethod is null)
                            {
                                usedAsIncDecTarget.Add(idFcr.Id);
                                usedAsScalarCompoundOrIncDecTarget.Add(idFcr.Id);
                            }
                            break;

                        case IArgumentOperation argOp when argOp.Parameter?.RefKind is not (null or RefKind.None) &&
                                                           UnwrapImplicitIdentityConversion(argOp.Value) is IFlowCaptureReferenceOperation argFcr:
                            usedAsPlaceOrLvalue.Add(argFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(argFcr.Id);
                            if (argOp.Parameter.RefKind is RefKind.Ref or RefKind.Out)
                            {
                                usedMutablyAsPlace.Add(argFcr.Id);
                                if (argOp.Parameter.RefKind == RefKind.Out)
                                {
                                    usedAsOutArg.Add(argFcr.Id);
                                }
                            }
                            break;

                        case IInvocationOperation invOp when invOp.Instance is not null &&
                                                             UnwrapImplicitIdentityConversion(invOp.Instance) is IFlowCaptureReferenceOperation invFcr:
                            if (!IsConsumingByValueReceiverInvocation(invOp))
                            {
                                usedAsPlaceOrLvalue.Add(invFcr.Id);
                                usedOutsideIsPatternAsPlace.Add(invFcr.Id);
                                if (IsMutatingReceiverInvocation(invOp))
                                {
                                    usedMutablyAsPlace.Add(invFcr.Id);
                                }
                            }
                            else if (ReadsReceiverAfterArguments(invOp))
                            {
                                movedReceiverOfCall.Add(invFcr.Id);
                            }
                            break;

                        case IPropertyReferenceOperation propOp when propOp.Instance is not null &&
                                                                     UnwrapImplicitIdentityConversion(propOp.Instance) is IFlowCaptureReferenceOperation propFcr:
                            usedAsPlaceOrLvalue.Add(propFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(propFcr.Id);
                            if (IsMutatingPropertyReceiverUse(propOp, usedMutablyAsPlace))
                            {
                                usedMutablyAsPlace.Add(propFcr.Id);
                            }
                            break;

                        case IFieldReferenceOperation fldOp when fldOp.Instance is not null &&
                                                                 UnwrapImplicitIdentityConversion(fldOp.Instance) is IFlowCaptureReferenceOperation fldFcr:
                            if (!IsWrapperFieldUnwrap(fldOp))
                            {
                                usedAsPlaceOrLvalue.Add(fldFcr.Id);
                                IOperation topField = fldOp;
                                while (topField.Parent is IFieldReferenceOperation pfr && pfr.Instance == topField)
                                {
                                    topField = pfr;
                                }
                                if (!(topField.Parent is IIsPatternOperation topPat && topPat.Value == topField))
                                {
                                    usedOutsideIsPatternAsPlace.Add(fldFcr.Id);
                                    if (IsPlaceExpressionMutatedAtParent(topField, usedMutablyAsPlace))
                                    {
                                        usedMutablyAsPlace.Add(fldFcr.Id);
                                    }
                                }
                            }
                            break;

                        case IImplicitIndexerReferenceOperation idxOp when UnwrapImplicitIdentityConversion(idxOp.Instance) is IFlowCaptureReferenceOperation idxFcr:
                            usedAsPlaceOrLvalue.Add(idxFcr.Id);
                            usedOutsideIsPatternAsPlace.Add(idxFcr.Id);
                            if (IsMutatingImplicitIndexerReceiverUse(idxOp, usedMutablyAsPlace))
                            {
                                usedMutablyAsPlace.Add(idxFcr.Id);
                            }
                            break;

                        case IIsPatternOperation patOp when UnwrapImplicitIdentityConversion(patOp.Value) is IFlowCaptureReferenceOperation patFcr:
                            usedAsPlaceOrLvalue.Add(patFcr.Id);
                            // Track the capture the arm actually tests, not the syntax of an initializer.
                            // A conditional or nested switch can define this same capture on several edges.
                            if (FindSwitchForPattern(patOp.Pattern.Syntax) is { } switchSyntax &&
                                IsBannedLinearUnionSwitch(switchSyntax) &&
                                !_bannedLinearUnionSwitchScrutineeRead.ContainsKey(patFcr.Id))
                            {
                                _bannedLinearUnionSwitchScrutineeRead.Add(patFcr.Id, false);
                            }
                            if (patOp.Pattern is not IDiscardPatternOperation)
                            {
                                patternUsesByCapture[patFcr.Id] = (patternUsesByCapture.TryGetValue(patFcr.Id, out int prevPatUses) ? prevPatUses : 0) + 1;
                            }
                            break;
                    }
                }
            }
        }

        bool captureUsageChanged = true;
        while (captureUsageChanged)
        {
            captureUsageChanged = false;
            foreach (var outerKvp in defsByCapture)
            {
                var outerCapId = outerKvp.Key;
                var defs = outerKvp.Value;
                bool outerAsPlace = usedAsPlaceOrLvalue.Contains(outerCapId);
                bool outerOutsidePat = usedOutsideIsPatternAsPlace.Contains(outerCapId);
                bool outerMut = usedMutablyAsPlace.Contains(outerCapId);
                bool outerRefBind = usedByRefBinding.Contains(outerCapId);

                foreach (var fc in defs)
                {
                    if (UnwrapImplicitIdentityConversion(fc.Value) is IFlowCaptureReferenceOperation directFcr)
                    {
                        if (outerAsPlace && usedAsPlaceOrLvalue.Add(directFcr.Id)) captureUsageChanged = true;
                        if (outerOutsidePat && usedOutsideIsPatternAsPlace.Add(directFcr.Id)) captureUsageChanged = true;
                        if (outerMut && usedMutablyAsPlace.Add(directFcr.Id)) captureUsageChanged = true;
                        if (outerRefBind && usedByRefBinding.Add(directFcr.Id)) captureUsageChanged = true;
                        if (refBindingMutable.TryGetValue(outerCapId, out bool rbm) && rbm &&
                            (!refBindingMutable.TryGetValue(directFcr.Id, out bool prevRbm) || !prevRbm))
                        {
                            refBindingMutable[directFcr.Id] = true;
                            captureUsageChanged = true;
                        }
                    }

                    foreach (var sub in fc.Value.DescendantsAndSelf())
                    {
                        switch (sub)
                        {
                            case IPropertyReferenceOperation propOp when propOp.Instance is not null &&
                                                                         UnwrapImplicitIdentityConversion(propOp.Instance) is IFlowCaptureReferenceOperation propFcr:
                                if (usedAsPlaceOrLvalue.Add(propFcr.Id)) captureUsageChanged = true;
                                if (usedOutsideIsPatternAsPlace.Add(propFcr.Id)) captureUsageChanged = true;
                                if (IsMutatingPropertyReceiverUse(propOp, usedMutablyAsPlace) &&
                                    usedMutablyAsPlace.Add(propFcr.Id))
                                {
                                    captureUsageChanged = true;
                                }
                                break;

                            case IFieldReferenceOperation fldOp when fldOp.Instance is not null &&
                                                                     UnwrapImplicitIdentityConversion(fldOp.Instance) is IFlowCaptureReferenceOperation fldFcr &&
                                                                     !IsWrapperFieldUnwrap(fldOp):
                            {
                                if (usedAsPlaceOrLvalue.Add(fldFcr.Id)) captureUsageChanged = true;
                                IOperation topField = fldOp;
                                while (topField.Parent is IFieldReferenceOperation pfr && pfr.Instance == topField)
                                {
                                    topField = pfr;
                                }
                                if (!(topField.Parent is IIsPatternOperation topPat && topPat.Value == topField))
                                {
                                    if (usedOutsideIsPatternAsPlace.Add(fldFcr.Id)) captureUsageChanged = true;
                                    if (IsPlaceExpressionMutatedAtParent(topField, usedMutablyAsPlace) &&
                                        usedMutablyAsPlace.Add(fldFcr.Id))
                                    {
                                        captureUsageChanged = true;
                                    }
                                }
                                break;
                            }

                            case IImplicitIndexerReferenceOperation idxOp when UnwrapImplicitIdentityConversion(idxOp.Instance) is IFlowCaptureReferenceOperation idxFcr:
                                if (usedAsPlaceOrLvalue.Add(idxFcr.Id)) captureUsageChanged = true;
                                if (usedOutsideIsPatternAsPlace.Add(idxFcr.Id)) captureUsageChanged = true;
                                if (IsMutatingImplicitIndexerReceiverUse(idxOp, usedMutablyAsPlace) &&
                                    usedMutablyAsPlace.Add(idxFcr.Id))
                                {
                                    captureUsageChanged = true;
                                }
                                break;
                        }
                    }
                }
            }
        }

        var nonDeclUsingCaptures = new Dictionary<CaptureId, ControlFlowRegion>();
        CollectNonDeclarationUsingCaptures(cfg, cfg.Root, defsByCapture, nonDeclUsingCaptures);
        var mappedNonDeclUsingLocals = new HashSet<int>();

        foreach (var kvp in defsByCapture)
        {
            var capId = kvp.Key;
            var defs = kvp.Value;
            var firstVal = defs[0].Value;
            var span = VibraniumSymbolHelpers.ToVibraniumSpan(firstVal.Syntax);

            if (defs.Count == 1 && nonDeclUsingCaptures.TryGetValue(capId, out var nonDeclUsingRegion))
            {
                var unwrappedUsingOp = UnwrapUsingResourceOperand(firstVal);
                if (unwrappedUsingOp is ILocalReferenceOperation usingLr &&
                    _localMap.TryGetValue(usingLr.Local, out int existingUsingLid))
                {
                    _captures[capId] = new CaptureInfo { Kind = CaptureKind.UsingExistingPlace };
                    var substLocType = SubstituteType(usingLr.Local.Type);
                    if (!usingLr.Local.IsUsing &&
                        substLocType is not null &&
                        VibraniumSymbolHelpers.IsLinearResourceSymbol(substLocType) &&
                        mappedNonDeclUsingLocals.Add(existingUsingLid))
                    {
                        _tryFinallyUsingLocal[nonDeclUsingRegion] = (existingUsingLid, substLocType);
                    }
                    continue;
                }
                if (unwrappedUsingOp is IParameterReferenceOperation usingPr &&
                    (_paramMap.TryGetValue(usingPr.Parameter, out int existingParamLid) ||
                     _paramMap.TryGetValue(usingPr.Parameter.OriginalDefinition, out existingParamLid)))
                {
                    _captures[capId] = new CaptureInfo { Kind = CaptureKind.UsingExistingPlace };
                    var substParamType = SubstituteType(usingPr.Parameter.Type);
                    if (usingPr.Parameter.RefKind == RefKind.None &&
                        substParamType is not null &&
                        VibraniumSymbolHelpers.IsLinearResourceSymbol(substParamType) &&
                        mappedNonDeclUsingLocals.Add(existingParamLid))
                    {
                        _tryFinallyUsingLocal[nonDeclUsingRegion] = (existingParamLid, substParamType);
                    }
                    continue;
                }

                var substExprType = SubstituteType(unwrappedUsingOp.Type ?? firstVal.Type);
                var exprMirType = MapMirType(substExprType);
                int exprTempLoc = CreateTempLocal(exprMirType, span, "__cap_val");
                _captures[capId] = new CaptureInfo
                {
                    Kind = CaptureKind.ValueTemp,
                    PrimaryLocalId = exprTempLoc,
                    MirType = exprMirType,
                    SingleValueOp = unwrappedUsingOp,
                };
                if (substExprType is not null && VibraniumSymbolHelpers.IsLinearResourceSymbol(substExprType))
                {
                    _tryFinallyUsingLocal[nonDeclUsingRegion] = (exprTempLoc, substExprType);
                }
                continue;
            }

            var unwrappedFirst = UnwrapImplicitIdentityConversion(firstVal);
            if (defs.Count == 1 &&
                unwrappedFirst is IInvocationOperation { TargetMethod.Name: "GetEnumerator" } utf8EnumInv &&
                TryGetUtf8ScanEnumeratorMode(utf8EnumInv, out int utf8ScanMode))
            {
                var roByteSpanT = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                int srcLocal = CreateTempLocal(roByteSpanT, span, "__utf8_src");
                int posLocal = CreateTempLocal(MirType.Int32, span, "__utf8_pos");
                int startLocal = CreateTempLocal(MirType.Int32, span, "__utf8_start");
                int lenLocal = CreateTempLocal(MirType.Int32, span, "__utf8_len");
                _captures[capId] = new CaptureInfo
                {
                    Kind = CaptureKind.Utf8ScanEnumerator,
                    PrimaryLocalId = srcLocal,
                    SecondaryLocalId = posLocal,
                    TertiaryLocalId = startLocal,
                    QuaternaryLocalId = lenLocal,
                    ScanMode = utf8ScanMode,
                    MirType = roByteSpanT,
                    IsMutable = false,
                    SingleValueOp = utf8EnumInv,
                };
                continue;
            }

            if (defs.Count == 1 &&
                unwrappedFirst is IInvocationOperation { TargetMethod.Name: "GetEnumerator" } getEnumInv &&
                TryGetEnumerableElementType(getEnumInv, out var elemType, out bool sourceIsReadOnly))
            {
                bool needMutSpan = !sourceIsReadOnly && enumeratorElementBoundMutably.Contains(capId);
                var elemMirType = MapMirType(SubstituteType(elemType));
                var spanMirType = new MirType.SpanType(elemMirType, IsReadOnly: !needMutSpan);
                int spanLocal = CreateTempLocal(spanMirType, span, "__enum_span");
                _locals[spanLocal] = _locals[spanLocal] with { IsForeachLoan = true };
                int idxLocal = CreateTempLocal(MirType.Int32, span, "__enum_idx");
                _captures[capId] = new CaptureInfo
                {
                    Kind = CaptureKind.SpanEnumerator,
                    PrimaryLocalId = spanLocal,
                    SecondaryLocalId = idxLocal,
                    MirType = elemMirType,
                    IsMutable = needMutSpan,
                    SingleValueOp = getEnumInv,
                };
                continue;
            }

            var firstValMirType = MapMirType(SubstituteType(firstVal.Type));
            bool isCloneCaptureForWithExpr =
                firstVal.Syntax?.Parent is WithExpressionSyntax withSyn &&
                withSyn.Expression == firstVal.Syntax;
            if (isCloneCaptureForWithExpr)
            {
                EmitDiag(VibraniumDiagnosticIds.Unsupported, "'with' expressions are not supported in Vibranium.", VibraniumSymbolHelpers.ToVibraniumSpan(firstVal.Syntax!.Parent!));
            }

            if (defs.Count == 1 && !isCloneCaptureForWithExpr)
            {
                if (usedWithUserDefinedAddAssign.Contains(capId) && IsNonRefPropertyOrIndexer(unwrappedFirst))
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.Unsupported,
                        "Compound assignment with user-defined 'operator +=' on a non-ref property or indexer is not supported in Vibranium.",
                        span);
                }
                else if ((usedAsDirectSimpleAssignTarget.Contains(capId) ||
                          usedAsCompoundAssignTarget.Contains(capId) ||
                          usedAsIncDecTarget.Contains(capId)) &&
                         TryCreateSetterPropertyOrIndexerCapture(
                             unwrappedFirst,
                             firstValMirType,
                             needsOldValueRead: usedAsCompoundAssignTarget.Contains(capId) || usedAsIncDecTarget.Contains(capId),
                             span,
                             out var setterCapInfo))
                {
                    _captures[capId] = setterCapInfo;
                    continue;
                }
            }

            // A ref assignment used as a selector yields a place; copying its value here would
            // manufacture a move for non-Copy values and lose the access slot for the guard.
            if (defs.Count == 1 && firstVal.Syntax is { } selectorSyntax && FindSwitchForSelector(selectorSyntax) is not null &&
                IsPlaceThroughRefAssignment(firstVal))
            {
                _captures[capId] = new CaptureInfo
                {
                    Kind = CaptureKind.DirectPlace,
                    MirType = firstValMirType,
                    SingleValueOp = UnwrapSwitchSelectorPlace(firstVal),
                };
                continue;
            }

            bool preferEagerValueTemp =
                !usedAsPlaceOrLvalue.Contains(capId) &&
                firstValMirType is not MirType.SpanType { IsReadOnly: false } and not MirType.Utf8ScanViewType;

            if (defs.Count == 1 && !isCloneCaptureForWithExpr && !preferEagerValueTemp)
            {
                bool canBindRefToType =
                    (!_module.IsViewLike(firstValMirType) || (firstValMirType is MirType.SpanType && !IsInConditionalRefReassignment(firstVal))) &&
                    !(firstValMirType is MirType.StructType capSt &&
                      (capSt.IsRefStruct || (_module.Structs.TryGetValue(capSt.Name, out var capSdef) && capSdef.IsRefStruct)));

                if (IsSideEffectFreePlaceExpression(firstVal))
                {
                    bool isPureStackSlot = IsPureStackValuePlaceWithoutDerefOrHeap(firstVal);
                    bool skipEagerRefBind =
                        (initViaCaptureRef.Contains(capId) && isPureStackSlot) ||
                        (usedAsOutArg.Contains(capId) && isPureStackSlot) ||
                        (usedAsDirectSimpleAssignTarget.Contains(capId) && isPureStackSlot) ||
                        (usedAsScalarCompoundOrIncDecTarget.Contains(capId) && isPureStackSlot);

                    if (usedOutsideIsPatternAsPlace.Contains(capId) && canBindRefToType && !skipEagerRefBind)
                    {
                        bool reserveMut = usedMutablyAsPlace.Contains(capId) ||
                            (firstValMirType is MirType.SpanType { IsReadOnly: false } && !IsObviouslySharedReceiver(firstVal));
                        int rLoc = CreateTempRefLocal(firstValMirType, reserveMut, span, "__cap_rsv");
                        _captures[capId] = new CaptureInfo
                        {
                            Kind = CaptureKind.ReservedPlace,
                            PrimaryLocalId = rLoc,
                            MirType = firstValMirType,
                            IsMutable = reserveMut,
                            IsTwoPhaseBorrow = reserveMut,
                            SingleValueOp = firstVal,
                        };
                        continue;
                    }

                    _captures[capId] = new CaptureInfo
                    {
                        Kind = CaptureKind.DirectPlace,
                        MirType = firstValMirType,
                        SingleValueOp = firstVal,
                    };
                    continue;
                }

                if (canBindRefToType &&
                    (IsRefProducedOrProjectedPlaceExpression(unwrappedFirst, defsByCapture) || IsPlaceThroughRefAssignment(unwrappedFirst)))
                {
                    bool canBeMut = !IsReadOnlyRefProducedOrProjectedPlace(unwrappedFirst, defsByCapture);
                    bool reserveMut = usedMutablyAsPlace.Contains(capId) && canBeMut;
                    int rLoc = CreateTempRefLocal(firstValMirType, reserveMut, span, "__cap_rsv");
                    _captures[capId] = new CaptureInfo
                    {
                        Kind = CaptureKind.ReservedPlace,
                        PrimaryLocalId = rLoc,
                        MirType = firstValMirType,
                        IsMutable = reserveMut,
                        IsTwoPhaseBorrow = reserveMut,
                        SingleValueOp = firstVal,
                    };
                    continue;
                }
            }

            bool isMultiBranchRefExpr =
                usedByRefBinding.Contains(capId) ||
                (defs.Count > 1 &&
                 usedOutsideIsPatternAsPlace.Contains(capId) &&
                 defs.All(static d => d.Syntax?.Parent is RefExpressionSyntax));
            if (isMultiBranchRefExpr)
            {
                bool isMut = (refBindingMutable.TryGetValue(capId, out bool m) && m) || usedMutablyAsPlace.Contains(capId);
                var targetMirType = MapMirType(SubstituteType(firstVal.Type));
                int refLoc = CreateTempRefLocal(targetMirType, isMut, span, "__cap_ref");
                _captures[capId] = new CaptureInfo
                {
                    Kind = CaptureKind.MultiBranchRef,
                    PrimaryLocalId = refLoc,
                    MirType = targetMirType,
                    IsMutable = isMut,
                };
                continue;
            }

            var valMirType = MapMirType(SubstituteType(firstVal.Type));
            int tempLoc = CreateTempLocal(valMirType, span, "__cap_val");
            _captures[capId] = new CaptureInfo
            {
                Kind = CaptureKind.ValueTemp,
                StrictConstantSource = defs.Count == 1 ? firstVal : null,
                PrimaryLocalId = tempLoc,
                MirType = valMirType,
                PatternUseCount = patternUsesByCapture.TryGetValue(capId, out int patUseCnt) ? patUseCnt : 0,
                HoldsMovedPlace = defs.Count == 1 && movedReceiverOfCall.Contains(capId) && CanLowerAsDirectPlace(firstVal),
            };
        }

        foreach (var kvp in allRefsByCapture)
        {
            if (_captures.ContainsKey(kvp.Key))
            {
                continue;
            }
            var fcr = kvp.Value;
            var fcrSpan = VibraniumSymbolHelpers.ToVibraniumSpan(fcr.Syntax);
            var fcrMirType = MapMirType(SubstituteType(fcr.Type));
            int tempLoc = CreateTempLocal(fcrMirType, fcrSpan, "__cap_synth");
            _captures[kvp.Key] = new CaptureInfo
            {
                Kind = CaptureKind.ValueTemp,
                PrimaryLocalId = tempLoc,
                MirType = fcrMirType,
            };
        }
        // A compound target capture denotes both its location and its old value.
        // Roslyn may lower a branching RHS into earlier blocks than the eventual
        // compound operation, so its old value must be read at the capture itself.
        foreach (var capId in usedAsScalarCompoundOrIncDecTarget)
        {
            if (_captures.TryGetValue(capId, out var capture) &&
                capture.Kind is CaptureKind.DirectPlace or CaptureKind.ReservedPlace or CaptureKind.MultiBranchRef)
            {
                var captureSpan = VibraniumSymbolHelpers.ToVibraniumSpan(defsByCapture[capId][0].Syntax);
                capture.UpdateOldValueLocalId = CreateTempLocal(capture.MirType, captureSpan, "__cap_old");
            }
        }
    }

    private void CollectNonDeclarationUsingCaptures(
        ControlFlowGraph cfg,
        ControlFlowRegion region,
        Dictionary<CaptureId, List<IFlowCaptureOperation>> defsByCapture,
        Dictionary<CaptureId, ControlFlowRegion> output)
    {
        if (region.Kind == ControlFlowRegionKind.TryAndFinally && !_tryFinallyUsingLocal.ContainsKey(region))
        {
            var finallyRegion = region.NestedRegions.FirstOrDefault(r => r.Kind == ControlFlowRegionKind.Finally);
            if (finallyRegion is not null)
            {
                bool found = false;
                for (int ord = finallyRegion.FirstBlockOrdinal; ord <= finallyRegion.LastBlockOrdinal && ord < cfg.Blocks.Length && !found; ord++)
                {
                    foreach (var op in EnumerateBlockOperations(cfg.Blocks[ord]))
                    {
                        foreach (var sub in op.DescendantsAndSelf())
                        {
                            if (sub is IFlowCaptureReferenceOperation fcr &&
                                defsByCapture.TryGetValue(fcr.Id, out var fcDefs) &&
                                fcDefs.Count == 1 &&
                                fcDefs[0].Syntax?.AncestorsAndSelf().OfType<UsingStatementSyntax>().Any(static u => u.Declaration is null) == true)
                            {
                                output[fcr.Id] = region;
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                }
            }
        }

        foreach (var nested in region.NestedRegions)
        {
            if (nested.Kind is ControlFlowRegionKind.Finally or ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter)
            {
                continue;
            }
            CollectNonDeclarationUsingCaptures(cfg, nested, defsByCapture, output);
        }
    }

    private IOperation UnwrapUsingResourceOperand(IOperation op)
    {
        var cur = UnwrapImplicitIdentityConversion(op);
        while (cur is IConversionOperation { IsImplicit: true, Conversion.IsUserDefined: false, Conversion.MethodSymbol: null } conv &&
               (SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type) ||
                conv.Type?.SpecialType == SpecialType.System_IDisposable))
        {
            cur = UnwrapImplicitIdentityConversion(conv.Operand);
        }
        return cur;
    }

    private void MapTryFinallyUsingRegions(ControlFlowGraph cfg, ControlFlowRegion region)
    {
        if (region.Kind == ControlFlowRegionKind.TryAndFinally)
        {
            var finallyRegion = region.NestedRegions.FirstOrDefault(r => r.Kind == ControlFlowRegionKind.Finally);
            if (finallyRegion is not null)
            {
                for (int ord = finallyRegion.FirstBlockOrdinal; ord <= finallyRegion.LastBlockOrdinal && ord < cfg.Blocks.Length; ord++)
                {
                    foreach (var op in EnumerateBlockOperations(cfg.Blocks[ord]))
                    {
                        foreach (var sub in op.DescendantsAndSelf())
                        {
                            if (sub is ILocalReferenceOperation { Local: { IsUsing: true } uLoc } &&
                                _localMap.TryGetValue(uLoc, out int lid))
                            {
                                _tryFinallyUsingLocal[region] = (lid, SubstituteType(uLoc.Type)!);
                                break;
                            }
                        }
                        if (_tryFinallyUsingLocal.ContainsKey(region)) break;
                    }
                    if (_tryFinallyUsingLocal.ContainsKey(region)) break;
                }
            }
        }

        foreach (var nested in region.NestedRegions)
        {
            if (nested.Kind is ControlFlowRegionKind.Finally or ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter)
            {
                continue;
            }
            MapTryFinallyUsingRegions(cfg, nested);
        }
    }

    private void RegisterRegionLocals(ControlFlowRegion region)
    {
        if (region.Kind is ControlFlowRegionKind.Finally or ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter)
        {
            return;
        }

        foreach (var loc in region.Locals)
        {
            GetOrCreateLocalId(loc, isPatternBindingOverride: null);
        }

        foreach (var nested in region.NestedRegions)
        {
            RegisterRegionLocals(nested);
        }
    }

    private static IEnumerable<IOperation> EnumerateBlockOperations(BasicBlock block)
    {
        foreach (var op in block.Operations) yield return op;
        if (block.BranchValue is not null) yield return opOrValue(block.BranchValue);
        static IOperation opOrValue(IOperation x) => x;
    }

    private static bool IsInsideFinallyOrCatchRegion(ControlFlowRegion? region)
    {
        while (region is not null)
        {
            if (region.Kind is ControlFlowRegionKind.Finally or ControlFlowRegionKind.Catch or ControlFlowRegionKind.Filter)
            {
                return true;
            }
            region = region.EnclosingRegion;
        }
        return false;
    }

    private static IOperation UnwrapImplicitIdentityConversion(IOperation op)
    {
        while (op is IConversionOperation conv &&
               conv.IsImplicit &&
               !conv.Conversion.IsUserDefined &&
               conv.Conversion.MethodSymbol is null &&
               SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type))
        {
            op = conv.Operand;
        }
        return op;
    }

    private bool TryGetUtf8ScanEnumeratorMode(IInvocationOperation getEnumInv, out int scanMode)
    {
        var enumType = SubstituteType(getEnumInv.Type);
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(enumType, "Utf8Lines"))
        {
            scanMode = 0;
            return true;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(enumType, "Utf8ArgEnumerator"))
        {
            scanMode = 1;
            return true;
        }
        scanMode = 0;
        return false;
    }

    private static bool TryGetEnumerableElementType(
        IInvocationOperation getEnumInv,
        out ITypeSymbol elementType,
        out bool sourceIsReadOnly)
    {
        elementType = null!;
        sourceIsReadOnly = false;
        var recvType = getEnumInv.Instance?.Type;
        if (recvType is null && getEnumInv.Arguments.Length > 0)
        {
            recvType = getEnumInv.Arguments[0].Value.Type;
        }
        if (recvType is null) return false;

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(recvType, "Vec") && recvType is INamedTypeSymbol vecNamed && vecNamed.TypeArguments.Length == 1)
        {
            elementType = vecNamed.TypeArguments[0];
            if (getEnumInv.Instance is not null && IsObviouslySharedReceiver(getEnumInv.Instance))
            {
                sourceIsReadOnly = true;
            }
            return true;
        }

        if (VibraniumSymbolHelpers.IsSpanType(recvType, out var spanElem, out bool spanRo))
        {
            elementType = spanElem;
            sourceIsReadOnly = spanRo;
            return true;
        }

        return false;
    }

    private static bool IsObviouslySharedReceiver(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        return op switch
        {
            IParameterReferenceOperation pr => pr.Parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter,
            ILocalReferenceOperation lr => lr.Local.RefKind == RefKind.RefReadOnly,
            IFieldReferenceOperation fr => fr.Field.IsReadOnly || (fr.Instance is not null && IsObviouslySharedReceiver(fr.Instance)),
            IInstanceReferenceOperation ir => ir.Type?.TypeKind == TypeKind.Class,
            _ => false,
        };
    }

    private static bool IsSideEffectFreePlaceExpression(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        if (op.ConstantValue.HasValue) return false;
        return op switch
        {
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IInstanceReferenceOperation => true,
            IFieldReferenceOperation fr => fr.Field.ContainingType is not null &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Some") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Ok") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Err") &&
                (fr.Instance is null || IsSideEffectFreePlaceExpression(fr.Instance)),
            _ => false,
        };
    }

    private bool IsPureStackValuePlaceWithoutDerefOrHeap(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        if (op.ConstantValue.HasValue) return false;
        switch (op)
        {
            case ILocalReferenceOperation lr:
                return !lr.Local.IsRef;

            case IParameterReferenceOperation pr:
                return pr.Parameter.RefKind is RefKind.None or RefKind.Out or RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter &&
                       !IsCapturedPrimaryConstructorParameter(pr.Parameter);

            case IInstanceReferenceOperation ir:
                return _thisLocalId is null ||
                       (SubstituteType(ir.Type)?.TypeKind == TypeKind.Struct &&
                        _currentSignature?.Params.FirstOrDefault()?.PassKind == MirPassKind.ByValue);

            case IFieldReferenceOperation fr:
                if (_thisLocalId is null &&
                    fr.Instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } &&
                    (_ctorFieldLocals.ContainsKey(fr.Field) || _ctorFieldLocals.ContainsKey(fr.Field.OriginalDefinition)))
                {
                    return true;
                }
                return fr.Instance is not null &&
                       SubstituteType(fr.Instance.Type)?.TypeKind == TypeKind.Struct &&
                       IsPureStackValuePlaceWithoutDerefOrHeap(fr.Instance);

            default:
                return false;
        }
    }

    private static bool IsNonRefPropertyOrIndexer(IOperation op)
    {
        op = UnwrapImplicitIdentityConversion(op);
        return op switch
        {
            IPropertyReferenceOperation p => !p.Property.ReturnsByRef && !p.Property.ReturnsByRefReadonly && !IsVecOrSpanIndexerAccess(p),
            IImplicitIndexerReferenceOperation impl =>
                !IsVecOrSpanIndexerAccess(impl) &&
                impl.IndexerSymbol is IPropertySymbol { ReturnsByRef: false, ReturnsByRefReadonly: false },
            _ => false,
        };
    }

    private bool TryCreateSetterPropertyOrIndexerCapture(
        IOperation unwrappedFirst,
        MirType valMirType,
        bool needsOldValueRead,
        VibraniumSpan span,
        out CaptureInfo info)
    {
        info = null!;

        if (unwrappedFirst is IPropertyReferenceOperation
            {
                Property:
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    SetMethod: { } propSetter,
                } prop,
            } propRef &&
            !IsVecOrSpanIndexerAccess(propRef) &&
            propSetter.ContainingType is INamedTypeSymbol propOwner &&
            IsInCompilationOrVibraniumStd(propOwner.ContainingAssembly))
        {
            if (needsOldValueRead && prop.GetMethod is null)
            {
                return false;
            }
            int oldLoc = needsOldValueRead ? CreateTempLocal(valMirType, span, "__cap_prop_old") : -1;
            info = new CaptureInfo
            {
                Kind = CaptureKind.SetterPropertyOrIndexer,
                PrimaryLocalId = oldLoc,
                MirType = valMirType,
                SingleValueOp = propRef,
                NeedsOldValueRead = needsOldValueRead,
            };
            return true;
        }

        if (unwrappedFirst is IImplicitIndexerReferenceOperation
            {
                IndexerSymbol: IPropertySymbol
                {
                    ReturnsByRef: false,
                    ReturnsByRefReadonly: false,
                    SetMethod: { } implSetter,
                } implProp,
                LengthSymbol: not null,
            } implRef &&
            !IsVecOrSpanIndexerAccess(implRef) &&
            SubstituteType(implRef.Argument.Type)?.ToDisplayString() == "System.Index" &&
            implSetter.ContainingType is INamedTypeSymbol implOwner &&
            IsInCompilationOrVibraniumStd(implOwner.ContainingAssembly))
        {
            if (needsOldValueRead && implProp.GetMethod is null)
            {
                return false;
            }
            int oldLoc = needsOldValueRead ? CreateTempLocal(valMirType, span, "__cap_idx_old") : -1;
            info = new CaptureInfo
            {
                Kind = CaptureKind.SetterPropertyOrIndexer,
                PrimaryLocalId = oldLoc,
                MirType = valMirType,
                SingleValueOp = implRef,
                NeedsOldValueRead = needsOldValueRead,
            };
            return true;
        }

        return false;
    }

    private bool IsRefProducedOrProjectedPlaceExpression(
        IOperation op,
        Dictionary<CaptureId, List<IFlowCaptureOperation>>? defsByCapture = null)
    {
        op = UnwrapImplicitIdentityConversion(op);
        switch (op)
        {
            case IFlowCaptureReferenceOperation fcr
                when defsByCapture is not null &&
                     defsByCapture.TryGetValue(fcr.Id, out var fcrDefs) &&
                     fcrDefs.Count > 0 &&
                     fcrDefs.All(d => IsSideEffectFreePlaceExpression(d.Value) || IsRefProducedOrProjectedPlaceExpression(d.Value, defsByCapture)):
                return true;

            case IPropertyReferenceOperation { Property.IsIndexer: true, Arguments.Length: 1 } propIdx
                when IsVecOrSpanIndexerAccess(propIdx):
                return true;

            case IPropertyReferenceOperation { Property.GetMethod: { } gm }
                when (gm.ReturnsByRef || gm.ReturnsByRefReadonly) &&
                     gm.ContainingType is INamedTypeSymbol owner &&
                     IsInCompilationOrVibraniumStd(owner.ContainingAssembly):
                return true;

            case IImplicitIndexerReferenceOperation implIdx
                when SubstituteType(implIdx.Argument.Type)?.ToDisplayString() == "System.Index":
                if (IsVecOrSpanIndexerAccess(implIdx))
                {
                    return true;
                }
                return implIdx.IndexerSymbol is IPropertySymbol { GetMethod: { } igm } &&
                       (igm.ReturnsByRef || igm.ReturnsByRefReadonly) &&
                       implIdx.LengthSymbol is not null &&
                       igm.ContainingType is INamedTypeSymbol iOwner &&
                       IsInCompilationOrVibraniumStd(iOwner.ContainingAssembly);

            case IInvocationOperation { TargetMethod.Name: "UnwrapMut", Instance: not null } unwrapMutInv
                when VibraniumSymbolHelpers.IsVibraniumCoreType(SubstituteType(unwrapMutInv.TargetMethod.ContainingType), "Option"):
                return true;

            case IInvocationOperation inv
                when (inv.TargetMethod.ReturnsByRef || inv.TargetMethod.ReturnsByRefReadonly) &&
                     inv.TargetMethod.ContainingType is INamedTypeSymbol cOwner &&
                     IsInCompilationOrVibraniumStd(cOwner.ContainingAssembly):
                return true;

            case IFieldReferenceOperation fr
                when !IsWrapperFieldUnwrap(fr) &&
                     fr.Instance is not null &&
                     (IsSideEffectFreePlaceExpression(fr.Instance) || IsRefProducedOrProjectedPlaceExpression(fr.Instance, defsByCapture)):
                return true;

            default:
                return false;
        }
    }

    private bool IsReadOnlyRefProducedOrProjectedPlace(
        IOperation op,
        Dictionary<CaptureId, List<IFlowCaptureOperation>>? defsByCapture = null)
    {
        op = UnwrapImplicitIdentityConversion(op);
        switch (op)
        {
            case IFlowCaptureReferenceOperation fcr
                when defsByCapture is not null &&
                     defsByCapture.TryGetValue(fcr.Id, out var fcrDefs):
                return fcrDefs.Any(d => IsReadOnlyRefProducedOrProjectedPlace(d.Value, defsByCapture));

            case IPropertyReferenceOperation { Property.IsIndexer: true } propIdx
                when VibraniumSymbolHelpers.IsSpanType(SubstituteType(propIdx.Property.ContainingType), out _, out bool spanRo):
                return spanRo;

            case IPropertyReferenceOperation { Property.GetMethod: { } gm }
                when gm.ReturnsByRefReadonly:
                return true;

            case IImplicitIndexerReferenceOperation implIdx:
                if (VibraniumSymbolHelpers.IsSpanType(SubstituteType(implIdx.Instance.Type), out _, out bool implSpanRo))
                {
                    return implSpanRo;
                }
                return implIdx.IndexerSymbol is IPropertySymbol { GetMethod.ReturnsByRefReadonly: true };

            case IInvocationOperation inv when inv.TargetMethod.ReturnsByRefReadonly:
                return true;

            case IFieldReferenceOperation fr:
                return fr.Field.IsReadOnly ||
                       (fr.Instance is not null &&
                        SubstituteType(fr.Instance.Type)?.TypeKind == TypeKind.Struct &&
                        IsReadOnlyRefProducedOrProjectedPlace(fr.Instance, defsByCapture));

            default:
                return false;
        }
    }

    private static bool IsWrapperFieldUnwrap(IFieldReferenceOperation fldOp)
    {
        var owner = fldOp.Field.ContainingType;
        return owner is not null &&
               (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Some") ||
                VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Ok") ||
                VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Err"));
    }

    // The Option and Result members that read their receiver after their arguments (F14; D1-W once D1 has moved it).
    private static bool ReadsReceiverAfterArguments(IInvocationOperation invOp) => invOp.TargetMethod.Name is "UnwrapOr" or "Expect";

    private bool IsConsumingByValueReceiverInvocation(IInvocationOperation invOp)
    {
        var owner = SubstituteType(invOp.TargetMethod.ContainingType);
        // F14: UnwrapOr and Expect read a Copy receiver in place, after their arguments.
        if (ReadsReceiverAfterArguments(invOp) && _module.IsCopy(MapMirType(owner)))
        {
            return false;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Option") &&
            invOp.TargetMethod.Name is "Unwrap" or "Expect" or "UnwrapOr" or "UnwrapOrDefault" or "IsNone")
        {
            return true;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Result") &&
            invOp.TargetMethod.Name is "Unwrap" or "Expect" or "UnwrapOr" or "UnwrapOrDefault" or "IsErr")
        {
            return true;
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Utf8Lines") &&
            invOp.TargetMethod.Name == "GetEnumerator")
        {
            return true;
        }
        return false;
    }

    private bool IsMutatingReceiverInvocation(IInvocationOperation invOp)
    {
        var method = SubstituteMethod(invOp.TargetMethod);
        var owner = method.ContainingType;
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec"))
        {
            return method.Name is "Push" or "Add" or "Pop" or "Clear" or "Truncate" or "Reserve" or "Extend" or "Resize" or "Slice" or "AsSpan";
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Option"))
        {
            return method.Name == "UnwrapMut";
        }
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "OptionMut"))
        {
            return method.Name is "Set" or "Take" or "Replace";
        }
        return VibraniumSymbolHelpers.GetReceiverPassKind(method) == MirPassKind.ImplicitThisMut;
    }

    private bool IsMutatingPropertyReceiverUse(
        IPropertyReferenceOperation propOp,
        HashSet<CaptureId>? mutablyUsedCaptures = null)
    {
        var owner = SubstituteType(propOp.Property.ContainingType);
        bool isTargetWrite = IsPlaceExpressionMutatedAtParent(propOp, mutablyUsedCaptures);
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(owner, "Vec"))
        {
            return propOp.Property.IsIndexer && isTargetWrite;
        }
        bool getMut = propOp.Property.GetMethod is { } gm &&
            VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(gm)) == MirPassKind.ImplicitThisMut;
        bool setMut = isTargetWrite && propOp.Property.SetMethod is { } sm &&
            VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(sm)) == MirPassKind.ImplicitThisMut;
        return getMut || setMut;
    }

    private bool IsMutatingImplicitIndexerReceiverUse(
        IImplicitIndexerReferenceOperation idxOp,
        HashSet<CaptureId>? mutablyUsedCaptures = null)
    {
        var recvType = SubstituteType(idxOp.Instance.Type);
        bool isTargetWrite = IsPlaceExpressionMutatedAtParent(idxOp, mutablyUsedCaptures);
        bool isRange = SubstituteType(idxOp.Argument.Type)?.ToDisplayString() == "System.Range";
        if (VibraniumSymbolHelpers.IsVibraniumCoreType(recvType, "Vec"))
        {
            return isRange || isTargetWrite;
        }
        bool idxMut = idxOp.IndexerSymbol switch
        {
            IMethodSymbol sm => VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(sm)) == MirPassKind.ImplicitThisMut,
            IPropertySymbol ps =>
                (ps.GetMethod is { } gm && VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(gm)) == MirPassKind.ImplicitThisMut) ||
                (isTargetWrite && ps.SetMethod is { } psm && VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(psm)) == MirPassKind.ImplicitThisMut),
            _ => false,
        };
        bool lenMut = idxOp.LengthSymbol is IPropertySymbol { GetMethod: { } lgm } &&
            VibraniumSymbolHelpers.GetReceiverPassKind(SubstituteMethod(lgm)) == MirPassKind.ImplicitThisMut;
        return idxMut || lenMut;
    }

    private bool IsPlaceExpressionMutatedAtParent(
        IOperation expr,
        HashSet<CaptureId>? mutablyUsedCaptures = null)
    {
        IOperation cur = expr;
        while (cur.Parent is IConversionOperation conv && SymbolEqualityComparer.Default.Equals(conv.Type, conv.Operand.Type))
        {
            cur = conv;
        }
        return cur.Parent switch
        {
            IFlowCaptureOperation fc => fc.Value == cur && mutablyUsedCaptures is not null && mutablyUsedCaptures.Contains(fc.Id),
            ISimpleAssignmentOperation { IsRef: false } sa => sa.Target == cur,
            ISimpleAssignmentOperation { IsRef: true } refSa =>
                refSa.Value == cur && (refSa.Target is not ILocalReferenceOperation lr || lr.Local.RefKind != RefKind.RefReadOnly),
            ICompoundAssignmentOperation ca => ca.Target == cur,
            IIncrementOrDecrementOperation id => id.Target == cur,
            IArgumentOperation arg => arg.Value == cur && arg.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            IInvocationOperation inv => inv.Instance == cur && IsMutatingReceiverInvocation(inv),
            IPropertyReferenceOperation prop => prop.Instance == cur && IsMutatingPropertyReceiverUse(prop, mutablyUsedCaptures),
            IImplicitIndexerReferenceOperation idx => idx.Instance == cur && IsMutatingImplicitIndexerReceiverUse(idx, mutablyUsedCaptures),
            IFieldReferenceOperation fld => fld.Instance == cur && !IsWrapperFieldUnwrap(fld) && IsPlaceExpressionMutatedAtParent(fld, mutablyUsedCaptures),
            _ => false,
        };
    }

    private void LowerStatementOperation(IOperation op)
    {
        switch (op)
        {
            case IExpressionStatementOperation exprStmt:
                LowerStatementOperation(exprStmt.Operation);
                break;

            case IFlowCaptureOperation fc:
                LowerFlowCapture(fc);
                break;

            case ISimpleAssignmentOperation assign:
                LowerSimpleAssignment(assign, wantResult: false);
                break;

            case ICompoundAssignmentOperation compound:
                LowerCompoundAssignment(compound, wantResult: false);
                break;

            case IIncrementOrDecrementOperation incDec:
                _ = LowerIncrementOrDecrement(incDec, wantResult: false);
                break;

            case IInvocationOperation inv:
                _ = LowerInvocation(inv, destPlace: null);
                break;

            case ILabeledOperation:
                foreach (var child in op.ChildOperations)
                {
                    LowerStatementOperation(child);
                }
                break;

            default:
                if (op.Type is not null && op.Type.SpecialType != SpecialType.System_Void)
                {
                    var mirT = MapMirType(SubstituteType(op.Type));
                    int discardTemp = CreateTempLocal(mirT, VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax));
                    var rv = LowerRvalue(op, mirT);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(discardTemp), rv, VibraniumSymbolHelpers.ToVibraniumSpan(op.Syntax)));
                }
                break;
        }
    }

    private void LowerFlowCapture(IFlowCaptureOperation fc)
    {
        if (!_captures.TryGetValue(fc.Id, out var info))
        {
            return;
        }

        var span = VibraniumSymbolHelpers.ToVibraniumSpan(fc.Syntax);
        var switchSyntax = FindSwitchForSelector(fc.Value.Syntax);
        MirPlace? matchPlace = null;
        switch (info.Kind)
        {
            case CaptureKind.Utf8ScanEnumerator:
            {
                var getEnumInv = (IInvocationOperation)info.SingleValueOp!;
                var roByteSpanT = new MirType.SpanType(MirType.UInt8, IsReadOnly: true);
                var viewType = info.ScanMode == 1 ? MirType.Utf8ArgEnumerator : MirType.Utf8Lines;
                IOperation viewExpr =
                    VibraniumSymbolHelpers.IsVibraniumCoreType(getEnumInv.TargetMethod.ContainingType, "Utf8Lines") &&
                    getEnumInv.TargetMethod.Name == "GetEnumerator" &&
                    getEnumInv.Instance is not null
                        ? getEnumInv.Instance
                        : getEnumInv;

                int viewTemp = CreateTempLocal(viewType, span, "__utf8_view");
                EmitAssignFromExpression(MirPlace.Local(viewTemp), viewExpr, viewType, span);
                _currentStmts.Add(new MirStatement.Intrinsic(
                    MirIntrinsicKind.Utf8ViewToSpan,
                    MirPlace.Local(info.PrimaryLocalId),
                    null,
                    false,
                    new MirOperand[] { new MirOperand.CopyOrMove(MirPlace.Local(viewTemp), viewType, span) },
                    Array.Empty<MirPlace>(),
                    roByteSpanT,
                    span));
                var zeroI32 = new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span);
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(info.SecondaryLocalId), new MirRvalue.Use(zeroI32), span));
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(info.TertiaryLocalId), new MirRvalue.Use(zeroI32), span));
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(info.QuaternaryLocalId), new MirRvalue.Use(zeroI32), span));
                break;
            }

            case CaptureKind.SpanEnumerator:
            {
                var getEnumInv = (IInvocationOperation)info.SingleValueOp!;
                var recvOp = UnwrapImplicitIdentityConversion(getEnumInv.Instance!);
                var recvType = SubstituteType(recvOp.Type)!;

                if (VibraniumSymbolHelpers.IsVibraniumCoreType(recvType, "Vec"))
                {
                    var vecPlace = LowerPlace(recvOp, isMutableAccess: info.IsMutable);
                    var intrKind = info.IsMutable ? MirIntrinsicKind.VecAsSpan : MirIntrinsicKind.VecAsReadOnlySpan;
                    _currentStmts.Add(new MirStatement.Intrinsic(
                        intrKind,
                        MirPlace.Local(info.PrimaryLocalId),
                        null,
                        false,
                        Array.Empty<MirOperand>(),
                        new[] { vecPlace },
                        _locals[info.PrimaryLocalId].Type,
                        span));
                }
                else if (VibraniumSymbolHelpers.IsSpanType(recvType, out _, out bool sourceReadOnly) && !sourceReadOnly)
                {
                    var sourceType = MapMirType(recvType);
                    var sourcePlace = LowerPlaceOrMaterializeTemp(recvOp, sourceType, isMutableAccess: info.IsMutable, span);
                    var resultType = _locals[info.PrimaryLocalId].Type;
                    if (info.IsMutable)
                    {
                        int lengthLocal = CreateTempLocal(MirType.Int32, span);
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.SpanLength, MirPlace.Local(lengthLocal), null, false,
                            Array.Empty<MirOperand>(), new[] { sourcePlace }, MirType.Int32, span));
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.SpanSlice, MirPlace.Local(info.PrimaryLocalId), null, false,
                            new MirOperand[]
                            {
                                new MirOperand.Const(new MirConstant.Int32Const(0), MirType.Int32, span),
                                new MirOperand.CopyOrMove(MirPlace.Local(lengthLocal), MirType.Int32, span),
                            },
                            new[] { sourcePlace }, resultType, span));
                    }
                    else
                    {
                        _currentStmts.Add(new MirStatement.Intrinsic(
                            MirIntrinsicKind.SpanToReadOnly, MirPlace.Local(info.PrimaryLocalId), null, false,
                            Array.Empty<MirOperand>(), new[] { sourcePlace }, resultType, span));
                    }
                }
                else
                {
                    var spanMirType = _locals[info.PrimaryLocalId].Type;
                    var rv = LowerRvalue(recvOp, spanMirType);
                    _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(info.PrimaryLocalId), rv, span));
                }

                _currentStmts.Add(new MirStatement.Assign(
                    MirPlace.Local(info.SecondaryLocalId),
                    new MirRvalue.Use(new MirOperand.Const(new MirConstant.Int32Const(-1), MirType.Int32, span)),
                    span));
                break;
            }

            case CaptureKind.DirectPlace:
                matchPlace = info.ResolvedDirectPlace = LowerPlace(
                    switchSyntax is not null ? UnwrapSwitchSelectorPlace(fc.Value) : fc.Value, isMutableAccess: false);
                break;

            case CaptureKind.SetterPropertyOrIndexer:
                LowerSetterPropertyOrIndexerFlowCapture(info, span);
                break;

            case CaptureKind.ReservedPlace:
            {
                var unwrappedVal = UnwrapImplicitIdentityConversion(fc.Value);
                if (!TryEmitRefProducingOperationInto(unwrappedVal, info.PrimaryLocalId, info.IsMutable, span, info.IsTwoPhaseBorrow))
                {
                    var place = LowerPlace(fc.Value, isMutableAccess: info.IsMutable, isTwoPhaseBorrow: info.IsTwoPhaseBorrow);
                    _currentStmts.Add(new MirStatement.BindRef(info.PrimaryLocalId, info.IsMutable, place, span, info.IsTwoPhaseBorrow));
                }
                matchPlace = new MirPlace(info.PrimaryLocalId, new MirProjection[] { new MirProjection.Deref(info.MirType) });
                break;
            }

            case CaptureKind.MultiBranchRef:
            {
                var unwrappedVal = UnwrapImplicitIdentityConversion(fc.Value);
                if (!TryEmitRefProducingOperationInto(unwrappedVal, info.PrimaryLocalId, info.IsMutable, span))
                {
                    var place = LowerPlace(fc.Value, isMutableAccess: info.IsMutable);
                    _currentStmts.Add(new MirStatement.BindRef(info.PrimaryLocalId, info.IsMutable, place, span));
                }
                break;
            }

            case CaptureKind.UsingExistingPlace:
                break;

            case CaptureKind.ValueTemp:
            {
                var value = info.SingleValueOp ?? fc.Value;
                if (switchSyntax is not null)
                {
                    value = UnwrapSwitchSelectorPlace(value);
                }
                int firstStatement = _currentStmts.Count;
                var rv = LowerRvalue(value, info.MirType);
                if (switchSyntax is not null)
                {
                    matchPlace = RecoverSwitchMatchPlace(value, rv, firstStatement);
                }
                if (info.HoldsMovedPlace && rv is MirRvalue.Use use)
                {
                    rv = use with { Operand = HoldMovedPlace(use.Operand, out info.MovedPlaceLocalId) };
                }
                _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(info.PrimaryLocalId), rv, span));
                break;
            }
        }
        if (switchSyntax is not null && matchPlace is not null)
        {
            BeginSwitchMatchBorrow(switchSyntax, matchPlace, info.MirType, span);
        }
        if (info.UpdateOldValueLocalId >= 0)
        {
            var oldPlace = info.Kind == CaptureKind.DirectPlace
                ? info.ResolvedDirectPlace!
                : new MirPlace(info.PrimaryLocalId, new MirProjection[] { new MirProjection.Deref(info.MirType) });
            _currentStmts.Add(new MirStatement.Assign(
                MirPlace.Local(info.UpdateOldValueLocalId),
                new MirRvalue.Use(new MirOperand.CopyOrMove(oldPlace, info.MirType, span)), span));
        }
    }

    private MirTerminator LowerBlockTerminator(
        ControlFlowGraph cfg,
        BasicBlock block,
        bool isConstructor,
        INamedTypeSymbol? ctorOwnerType,
        VibraniumSpan fnSpan,
        List<int> orderedBlockIds)
    {
        var span = block.BranchValue is not null
            ? VibraniumSymbolHelpers.ToVibraniumSpan(block.BranchValue.Syntax)
            : fnSpan;

        if (block.ConditionKind == ControlFlowConditionKind.None)
        {
            var fall = block.FallThroughSuccessor;
            if (fall is null || fall.Destination is null)
            {
                return new MirTerminator.Unreachable(span);
            }

            if (fall.Semantics == ControlFlowBranchSemantics.Return || fall.Destination.Kind == BasicBlockKind.Exit)
            {
                if (isConstructor && ctorOwnerType is not null)
                {
                    EmitLeavingRegionsInline(fall.LeavingRegions, span);
                    var instanceFields = VibraniumSymbolHelpers.GetInstanceFields(ctorOwnerType, _compilation);
                    return EmitConstructorReturnSequence(ctorOwnerType, instanceFields, span);
                }

                if (block.BranchValue is not null)
                {
                    if (IsRefReturnBlock(block))
                    {
                        bool retMut = _currentSignature?.ReturnRefIsMutable == true;
                        if (retMut && UnwrapImplicitIdentityConversion(block.BranchValue) is IParameterReferenceOperation retPr &&
                            IsCapturedPrimaryConstructorParameter(retPr.Parameter))
                        {
                            EmitDiag(VibraniumDiagnosticIds.MutateThroughSharedBorrow, $"Cannot return mutable reference to captured primary-constructor parameter '{retPr.Parameter.Name}'.", span);
                        }
                        var refPlace = LowerPlace(block.BranchValue, isMutableAccess: retMut);
                        EmitLeavingRegionsInline(fall.LeavingRegions, span);
                        return new MirTerminator.Return(null, refPlace, span);
                    }

                    var retType = MapMirType(SubstituteType(block.BranchValue.Type));
                    var retOp = LowerOperand(block.BranchValue, retType);
                    if (HasAnyExitingLocals(fall.LeavingRegions) && retOp is MirOperand.CopyOrMove)
                    {
                        int retTemp = CreateTempLocal(retType, span, "__ret");
                        _currentStmts.Add(new MirStatement.Assign(MirPlace.Local(retTemp), new MirRvalue.Use(retOp), span));
                        retOp = new MirOperand.CopyOrMove(MirPlace.Local(retTemp), retType, span);
                    }
                    EmitLeavingRegionsInline(fall.LeavingRegions, span);
                    return new MirTerminator.Return(retOp, null, span);
                }

                EmitLeavingRegionsInline(fall.LeavingRegions, span);
                return new MirTerminator.Return(null, null, span);
            }

            EmitLeavingRegionsInline(fall.LeavingRegions, span);
            return new MirTerminator.Goto(fall.Destination.Ordinal, span);
        }

        var condOp = block.BranchValue!;
        var fallBranch = block.FallThroughSuccessor!;
        var condBranch = block.ConditionalSuccessor!;

        ControlFlowBranch trueBranch = block.ConditionKind == ControlFlowConditionKind.WhenTrue ? condBranch : fallBranch;
        ControlFlowBranch falseBranch = block.ConditionKind == ControlFlowConditionKind.WhenTrue ? fallBranch : condBranch;

        while (condOp is IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } notOp)
        {
            condOp = notOp.Operand;
            (trueBranch, falseBranch) = (falseBranch, trueBranch);
        }

        if (condOp is IInvocationOperation guardErrInv &&
            guardErrInv.TargetMethod.Name == "IsErr" &&
            VibraniumSymbolHelpers.IsVibraniumCoreType(guardErrInv.TargetMethod.ContainingType, "Result") &&
            guardErrInv.Instance is not null &&
            guardErrInv.Arguments.Length == 2 &&
            IsDirectTopLevelIfCondition(guardErrInv.Syntax))
        {
            var resType = (MirType.ResultType)MapMirType(SubstituteType(guardErrInv.Instance.Type));
            var consumedRes = LowerOperand(guardErrInv.Instance, resType);
            int okLocalId = ExtractOutArgumentLocalId(guardErrInv.Arguments[0].Value, resType.Ok, span);
            int errLocalId = ExtractOutArgumentLocalId(guardErrInv.Arguments[1].Value, new MirType.ErrWrapperType(resType.Err), span);

            int errTarget = ResolveBranchTargetWithLeavingRegions(trueBranch, span, orderedBlockIds);
            int okTarget = ResolveBranchTargetWithLeavingRegions(falseBranch, span, orderedBlockIds);

            return new MirTerminator.GuardResultIsErr(consumedRes, okLocalId, errLocalId, errTarget, okTarget, span);
        }

        if (condOp is IInvocationOperation guardNoneInv &&
            guardNoneInv.TargetMethod.Name == "IsNone" &&
            VibraniumSymbolHelpers.IsVibraniumCoreType(guardNoneInv.TargetMethod.ContainingType, "Option") &&
            guardNoneInv.Instance is not null &&
            guardNoneInv.Arguments.Length == 1 &&
            IsDirectTopLevelIfCondition(guardNoneInv.Syntax))
        {
            var optType = (MirType.OptionType)MapMirType(SubstituteType(guardNoneInv.Instance.Type));
            var consumedOpt = LowerOperand(guardNoneInv.Instance, optType);
            int someValLocalId = ExtractOutArgumentLocalId(guardNoneInv.Arguments[0].Value, optType.Inner, span);

            int noneTarget = ResolveBranchTargetWithLeavingRegions(trueBranch, span, orderedBlockIds);
            int someTarget = ResolveBranchTargetWithLeavingRegions(falseBranch, span, orderedBlockIds);

            return new MirTerminator.GuardOptionIsNone(consumedOpt, someValLocalId, noneTarget, someTarget, span);
        }

        if (condOp is IIsPatternOperation isPat &&
            TryLowerUnionPatternTerminator(cfg, isPat, trueBranch, falseBranch, span, orderedBlockIds, out var unionTerm))
        {
            return unionTerm;
        }

        if (condOp is IIsPatternOperation { Pattern: IDiscardPatternOperation } discardPat &&
            UnwrapImplicitIdentityConversion(discardPat.Value) is IFlowCaptureReferenceOperation)
        {
            int discardTrueTarget = ResolveBranchTargetWithLeavingRegions(trueBranch, span, orderedBlockIds);
            return new MirTerminator.Goto(discardTrueTarget, span);
        }

        var boolOperand = LowerConditionOperand(condOp, span);
        int tBlock = ResolveBranchTargetWithLeavingRegions(trueBranch, span, orderedBlockIds);
        int fBlock = ResolveBranchTargetWithLeavingRegions(falseBranch, span, orderedBlockIds);
        // Roslyn CFG preserves constant arms of ?: as branches. Their impossible edges
        // must not contribute an uninitialized state to out-variable/pattern binding joins.
        if (boolOperand is MirOperand.Const { Value: MirConstant.BoolConst constant })
        {
            return new MirTerminator.Goto(constant.Value ? tBlock : fBlock, span);
        }
        if (IsExhaustiveStatementSwitchFinalTest(condOp))
        {
            return new MirTerminator.Goto(tBlock, span);
        }
        return new MirTerminator.Branch(boolOperand, tBlock, fBlock, span);
    }

    private bool IsExhaustiveStatementSwitchFinalTest(IOperation condition)
    {
        // Roslyn's public CFG retains the last label's unmatched edge even when its
        // source flow analysis proves that every selector enters a switch section.
        // Identify that dispatch test, never a when-guard or a condition in a body.
        if (!condition.IsImplicit ||
            condition.Syntax.AncestorsAndSelf().OfType<SwitchLabelSyntax>().FirstOrDefault() is not { } label ||
            label.Parent is not SwitchSectionSyntax section ||
            section.Parent is not SwitchStatementSyntax statement ||
            statement.Sections.SelectMany(s => s.Labels).LastOrDefault(l => l is not DefaultSwitchLabelSyntax) != label)
        {
            return false;
        }

        bool isLabelTest = label switch
        {
            CaseSwitchLabelSyntax valueLabel =>
                condition is (IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals } or IIsPatternOperation) &&
                valueLabel.Value.Span.Contains(condition.Syntax.Span),
            CasePatternSwitchLabelSyntax patternLabel =>
                condition is IIsPatternOperation && patternLabel.Pattern.Span.Contains(condition.Syntax.Span),
            _ => false,
        };
        if (!isLabelTest)
        {
            return false;
        }

        var sourceCompilation = _compilation;
        var byTree = s_exhaustiveStatementSwitches.GetValue(sourceCompilation, static _ => new());
        var proofs = byTree.GetValue(statement.SyntaxTree, tree =>
            new Lazy<HashSet<int>>(() => ProveExhaustiveStatementSwitches(sourceCompilation, tree))).Value;
        return proofs.Contains(statement.SpanStart);
    }

    private static HashSet<int> ProveExhaustiveStatementSwitches(CSharpCompilation sourceCompilation, SyntaxTree tree)
    {
        var proven = new HashSet<int>();
        var sourceModel = sourceCompilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var statements = root.DescendantNodes().OfType<SwitchStatementSyntax>().ToArray();
        foreach (var statement in statements)
        {
            var fallback = statement.Sections.FirstOrDefault(section => section.Labels.Any(label => label is DefaultSwitchLabelSyntax));
            if (fallback is null || fallback.Statements.Count == 0) continue;

            // The public operation CFG marks even some CS0162 default blocks as
            // reachable. Ask C# source flow analysis, not our own pattern coverage
            // calculation, whether that section can be entered. This also works
            // when CS0162 is suppressed and preserves existing runtime pruning.
            var flow = sourceModel.AnalyzeControlFlow(fallback.Statements.First(), fallback.Statements.Last());
            if (flow is { Succeeded: true, StartPointIsReachable: false }) proven.Add(statement.SpanStart);
        }
        if (sourceModel.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return proven;
        }

        var switches = statements
            .Where(s => s.Sections.Count > 0 && !s.Sections.SelectMany(section => section.Labels).Any(l => l is DefaultSwitchLabelSyntax)).ToArray();
        var names = new HashSet<string>(root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText));
        var markers = new Dictionary<SwitchStatementSyntax, (string Name, SyntaxAnnotation Annotation)>();
        var declarations = new Dictionary<BlockSyntax, List<StatementSyntax>>();
        var globals = new List<MemberDeclarationSyntax>();
        int nextName = 0;

        // A fresh marker outside the analyzed region is assigned at every section
        // entry. AlwaysAssigned resets its entry state, so the marker is assigned
        // on every exit exactly when no unmatched dispatch can leave the statement.
        // Keep all original scopes, labels, guards, and bodies: replacing a statement
        // with a switch expression would incorrectly prove union/enum coverage.
        foreach (var statement in switches)
        {
            string name;
            do { name = "__vibSwitchDispatch" + nextName++; } while (!names.Add(name));
            markers.Add(statement, (name, new SyntaxAnnotation("Vibranium.SwitchDispatch")));
            var declaration = SyntaxFactory.LocalDeclarationStatement(SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(name))));
            if (statement.Ancestors().OfType<BlockSyntax>().FirstOrDefault() is { } owner)
            {
                if (!declarations.TryGetValue(owner, out var locals))
                {
                    declarations.Add(owner, locals = new List<StatementSyntax>());
                }
                locals.Add(declaration);
            }
            else
            {
                globals.Add(SyntaxFactory.GlobalStatement(declaration));
            }
        }

        var proofRoot = (CompilationUnitSyntax)root.ReplaceNodes(switches.Cast<SyntaxNode>().Concat(declarations.Keys), (original, rewritten) =>
        {
            if (original is SwitchStatementSyntax statement)
            {
                var (name, annotation) = markers[statement];
                var assignment = SyntaxFactory.ExpressionStatement(SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression, SyntaxFactory.IdentifierName(name),
                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
                // Read only the marker, without affecting any user state or exit.
                // This keeps the proof valid even when CS0219 is an error.
                var read = SyntaxFactory.IfStatement(SyntaxFactory.IdentifierName(name), SyntaxFactory.Block());
                return ((SwitchStatementSyntax)rewritten).WithSections(SyntaxFactory.List(
                    ((SwitchStatementSyntax)rewritten).Sections.Select(section => section.WithStatements(
                        section.Statements.InsertRange(0, new StatementSyntax[] { assignment, read }))))).WithAdditionalAnnotations(annotation);
            }
            var block = (BlockSyntax)rewritten;
            return block.WithStatements(block.Statements.InsertRange(0, declarations[(BlockSyntax)original]));
        });
        proofRoot = proofRoot.WithMembers(proofRoot.Members.InsertRange(0, globals));
        var proofTree = tree.WithRootAndOptions(proofRoot, tree.Options);
        var proofModel = sourceCompilation.ReplaceSyntaxTree(tree, proofTree).GetSemanticModel(proofTree);
        if (proofModel.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return proven;
        }

        foreach (var entry in markers)
        {
            // AlwaysAssigned has no exit state for an all-infinite switch. The
            // original statement's unreachable endpoint is a complementary proof.
            var completion = sourceModel.AnalyzeControlFlow(entry.Key);
            var statement = (SwitchStatementSyntax)proofTree.GetRoot().GetAnnotatedNodes(entry.Value.Annotation).Single();
            var flow = proofModel.AnalyzeDataFlow(statement);
            if (completion is { Succeeded: true, EndPointIsReachable: false } ||
                (flow is { Succeeded: true } && flow.AlwaysAssigned.Any(s => s is ILocalSymbol && s.Name == entry.Value.Name)))
            {
                proven.Add(entry.Key.SpanStart);
            }
        }
        return proven;
    }

    private static bool IsRefReturnBlock(BasicBlock block)
    {
        if (block.BranchValue?.Syntax?.Parent is RefExpressionSyntax)
        {
            return true;
        }
        var retStmt = block.BranchValue?.Syntax?.AncestorsAndSelf().OfType<ReturnStatementSyntax>().FirstOrDefault();
        if (retStmt?.Expression is RefExpressionSyntax)
        {
            return true;
        }
        var arrow = block.BranchValue?.Syntax?.AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
        if (arrow?.Expression is RefExpressionSyntax)
        {
            return true;
        }
        return false;
    }

    private static bool IsDirectTopLevelIfCondition(SyntaxNode? syntax)
    {
        if (syntax is null)
        {
            return false;
        }
        SyntaxNode cur = syntax;
        while (cur.Parent is ParenthesizedExpressionSyntax ||
               (cur.Parent is PrefixUnaryExpressionSyntax pref && pref.IsKind(SyntaxKind.LogicalNotExpression)))
        {
            cur = cur.Parent;
        }
        return cur.Parent is IfStatementSyntax ifStmt && ifStmt.Condition == cur;
    }

    private int ExtractOutArgumentLocalId(IOperation argVal, MirType fallbackType, VibraniumSpan span)
    {
        switch (argVal)
        {
            case IDeclarationExpressionOperation { Expression: ILocalReferenceOperation lr }:
                return GetOrCreateLocalId(lr.Local, isPatternBindingOverride: null);
            case IDiscardOperation:
            case IDeclarationExpressionOperation { Expression: IDiscardOperation }:
                if (_module.IsLinearResource(fallbackType))
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        "Cannot discard linear IDisposable resource with 'out _'; bind and dispose it via 'using var'.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(argVal.Syntax));
                }
                return CreateTempLocal(fallbackType, span, "__discard_out");
            default:
                EmitDiag(
                    VibraniumDiagnosticIds.Unsupported,
                    "Every 'out' argument of 'IsErr'/'IsNone' must be a fresh inline declaration ('out var x') or discard ('out _').",
                    VibraniumSymbolHelpers.ToVibraniumSpan(argVal.Syntax));
                return CreateTempLocal(fallbackType, span, "__invalid_out");
        }
    }

    private bool TryLowerUnionPatternTerminator(
        ControlFlowGraph cfg,
        IIsPatternOperation isPat,
        ControlFlowBranch trueBranch,
        ControlFlowBranch falseBranch,
        VibraniumSpan span,
        List<int> orderedBlockIds,
        out MirTerminator terminator)
    {
        terminator = null!;
        var scrutTypeSym = SubstituteType(isPat.Value.Type);
        if (scrutTypeSym is null) return false;

        var scrutMirType = MapMirType(scrutTypeSym);

        if (RejectUnsupportedPattern(isPat.Pattern))
        {
            var unmatched = RejectedPatternResult(isPat.Pattern) ? trueBranch : falseBranch;
            terminator = new MirTerminator.Goto(ResolveBranchTargetWithLeavingRegions(unmatched, span, orderedBlockIds), span);
            return true;
        }

        if (scrutMirType is MirType.OptionType or MirType.OptionRefType or MirType.OptionMutType)
        {
            MirType innerType = scrutMirType switch
            {
                MirType.OptionType o => o.Inner,
                MirType.OptionRefType orf => orf.Inner,
                MirType.OptionMutType om => om.Inner,
                _ => MirType.Int32,
            };

            if (TryExtractOptionArm(isPat.Pattern, innerType, span, out bool isSomeArm, out int someBindingId, out var wrapperLocalSym, out var extraSubpattern))
            {
                ControlFlowBranch effectiveSomeBranch;
                ControlFlowBranch effectiveNoneBranch;
                ControlFlowBranch? collapsedAfterBothBranch = null;
                bool collapsed = false;
                ILocalSymbol? noneWrapperSym = isSomeArm ? null : wrapperLocalSym;
                if (!isSomeArm) wrapperLocalSym = null;

                if (isSomeArm)
                {
                    effectiveSomeBranch = trueBranch;
                    effectiveNoneBranch = falseBranch;

                    if (!HasSwitchCaseWhenGuard(isPat) &&
                        TryCollapseComplementaryOptionArm(cfg, isPat.Value, falseBranch, expectSome: false, arm1HasExtraSub: extraSubpattern is not null, innerType, span, out var collapsedNoneBranch, out _, out var collapsedNoneWrapper, out _, out var afterBoth))
                    {
                        effectiveNoneBranch = collapsedNoneBranch;
                        noneWrapperSym = collapsedNoneWrapper;
                        collapsedAfterBothBranch = afterBoth;
                        collapsed = true;
                    }
                }
                else
                {
                    effectiveNoneBranch = trueBranch;
                    effectiveSomeBranch = falseBranch;

                    if (!HasSwitchCaseWhenGuard(isPat) &&
                        TryCollapseComplementaryOptionArm(cfg, isPat.Value, falseBranch, expectSome: true, arm1HasExtraSub: extraSubpattern is not null, innerType, span, out var collapsedSomeBranch, out int arm2SomeId, out var arm2Wrapper, out var arm2ExtraSub, out var afterBoth))
                    {
                        effectiveSomeBranch = collapsedSomeBranch;
                        someBindingId = arm2SomeId;
                        wrapperLocalSym = arm2Wrapper;
                        extraSubpattern = arm2ExtraSub;
                        collapsedAfterBothBranch = afterBoth;
                        collapsed = true;
                    }
                }

                bool singleReadValueScrutinee = collapsed || !CaptureReferencedBeforeRedef(cfg, isPat.Value, falseBranch);
                var scrutinee = LowerMatchScrutinee(isPat.Value, scrutMirType, borrowMultiUseValueTemp: !singleReadValueScrutinee);
                var somePattern = isSomeArm ? isPat.Pattern : collapsed
                    ? (effectiveSomeBranch.Source.BranchValue as IIsPatternOperation)?.Pattern
                    : null;
                scrutinee = ConfigureOptionSwitchPatternMatch(scrutinee, isPat.Pattern, somePattern,
                    extraSubpattern is not null, ref someBindingId);
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

                bool consumedByVal = scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: false };
                MarkOwnedPatternBindingIfConsumed(someBindingId, consumedByVal);

                int someTarget = collapsed && !isSomeArm
                    ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, effectiveSomeBranch, span, orderedBlockIds)
                    : ResolveBranchTargetWithLeavingRegions(effectiveSomeBranch, span, orderedBlockIds);
                int noneTarget = collapsed && isSomeArm
                    ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, effectiveNoneBranch, span, orderedBlockIds)
                    : ResolveBranchTargetWithLeavingRegions(effectiveNoneBranch, span, orderedBlockIds);

                if (wrapperLocalSym is not null)
                {
                    int wrapperLocalId = GetOrCreateLocalId(wrapperLocalSym, isPatternBindingOverride: null);
                    MarkOwnedPatternBindingIfConsumed(wrapperLocalId, consumedByVal);
                    someTarget = PrependStatementViaTrampoline(
                        someTarget,
                        new MirStatement.Assign(
                            MirPlace.Local(wrapperLocalId),
                            new MirRvalue.MakeSomeWrapper(innerType,
                                AsSwitchPatternSelection(somePattern ?? isPat.Pattern, new MirOperand.CopyOrMove(MirPlace.Local(someBindingId), innerType, span))),
                            span),
                        span,
                        orderedBlockIds);
                }

                if (noneWrapperSym is not null)
                {
                    noneTarget = PrependStatementViaTrampoline(noneTarget, NoneDesignationAssign(noneWrapperSym, span, null), span, orderedBlockIds);
                }

                if (extraSubpattern is not null)
                {
                    int subFailTarget = collapsed && collapsedAfterBothBranch is not null
                        ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, collapsedAfterBothBranch, span, orderedBlockIds)
                        : noneTarget;
                    someTarget = IsRejectedLinearSwitchSubpattern(isPat.Pattern, scrutMirType)
                        ? subFailTarget
                        : BuildExtraSubpatternCheckBlock(someBindingId, innerType, consumedByVal, extraSubpattern, someTarget, subFailTarget, span, orderedBlockIds);
                }

                terminator = new MirTerminator.MatchOption(scrutinee, someBindingId, someTarget, noneTarget, span);
                return true;
            }
        }
        else if (scrutMirType is MirType.ResultType resType &&
                 TryExtractResultArm(isPat.Pattern, resType, span, out bool isOkArm, out int bindingId, out var wrapperSym, out var extraSubpattern))
        {
            int okBindingId;
            int errBindingId;
            ControlFlowBranch effectiveOkBranch;
            ControlFlowBranch effectiveErrBranch;
            ControlFlowBranch? collapsedAfterBothBranch = null;
            ILocalSymbol? okWrapperSym = null;
            ILocalSymbol? errWrapperSym = null;
            IReadOnlyList<IPatternOperation>? okExtraPat = null;
            IReadOnlyList<IPatternOperation>? errExtraPat = null;
            bool collapsed = false;

            if (isOkArm)
            {
                okBindingId = bindingId;
                okWrapperSym = wrapperSym;
                okExtraPat = extraSubpattern;
                effectiveOkBranch = trueBranch;
                effectiveErrBranch = falseBranch;

                if (!HasSwitchCaseWhenGuard(isPat) &&
                    TryCollapseComplementaryResultArm(cfg, isPat.Value, falseBranch, expectOk: false, arm1HasExtraSub: extraSubpattern is not null, resType, span, out var collapsedErrBranch, out int arm2ErrId, out var arm2ErrWrapper, out var arm2ErrExtra, out var afterBoth))
                {
                    effectiveErrBranch = collapsedErrBranch;
                    errBindingId = arm2ErrId;
                    errWrapperSym = arm2ErrWrapper;
                    errExtraPat = arm2ErrExtra;
                    collapsedAfterBothBranch = afterBoth;
                    collapsed = true;
                }
                else
                {
                    errBindingId = CreatePatternLocal(resType.Err, span, "__err_unused");
                }
            }
            else
            {
                errBindingId = bindingId;
                errWrapperSym = wrapperSym;
                errExtraPat = extraSubpattern;
                effectiveErrBranch = trueBranch;
                effectiveOkBranch = falseBranch;

                if (!HasSwitchCaseWhenGuard(isPat) &&
                    TryCollapseComplementaryResultArm(cfg, isPat.Value, falseBranch, expectOk: true, arm1HasExtraSub: extraSubpattern is not null, resType, span, out var collapsedOkBranch, out int arm2OkId, out var arm2OkWrapper, out var arm2OkExtra, out var afterBoth))
                {
                    effectiveOkBranch = collapsedOkBranch;
                    okBindingId = arm2OkId;
                    okWrapperSym = arm2OkWrapper;
                    okExtraPat = arm2OkExtra;
                    collapsedAfterBothBranch = afterBoth;
                    collapsed = true;
                }
                else
                {
                    okBindingId = CreatePatternLocal(resType.Ok, span, "__ok_unused");
                }
            }

            bool singleReadValueScrutinee = collapsed || !CaptureReferencedBeforeRedef(cfg, isPat.Value, falseBranch);
            var scrutinee = LowerMatchScrutinee(isPat.Value, resType, borrowMultiUseValueTemp: !singleReadValueScrutinee);
            if (_module.IsLinearResource(resType) && scrutinee is MirMatchScrutinee.PlaceScrutinee)
            {
                EmitDiag(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Cannot borrow-match linear Result<T, E> in place; consume it via '.IsErr(out ..., out ...)', '.Unwrap()', or 'Move(...)'.",
                    span);
            }

            bool consumedByVal = scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: false };
            MarkOwnedPatternBindingIfConsumed(okBindingId, consumedByVal);
            MarkOwnedPatternBindingIfConsumed(errBindingId, consumedByVal);

            int okTarget = collapsed && !isOkArm
                ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, effectiveOkBranch, span, orderedBlockIds)
                : ResolveBranchTargetWithLeavingRegions(effectiveOkBranch, span, orderedBlockIds);
            int errTarget = collapsed && isOkArm
                ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, effectiveErrBranch, span, orderedBlockIds)
                : ResolveBranchTargetWithLeavingRegions(effectiveErrBranch, span, orderedBlockIds);

            if (okWrapperSym is not null)
            {
                int wId = GetOrCreateLocalId(okWrapperSym, isPatternBindingOverride: null);
                MarkOwnedPatternBindingIfConsumed(wId, consumedByVal);
                okTarget = PrependStatementViaTrampoline(
                    okTarget,
                    new MirStatement.Assign(
                        MirPlace.Local(wId),
                        new MirRvalue.MakeOkWrapper(resType.Ok,
                            AsSwitchPatternSelection(isPat.Pattern, new MirOperand.CopyOrMove(MirPlace.Local(okBindingId), resType.Ok, span))),
                        span),
                    span,
                    orderedBlockIds);
            }

            if (errWrapperSym is not null)
            {
                int wId = GetOrCreateLocalId(errWrapperSym, isPatternBindingOverride: null);
                MarkOwnedPatternBindingIfConsumed(wId, consumedByVal);
                errTarget = PrependStatementViaTrampoline(
                    errTarget,
                    new MirStatement.Assign(
                        MirPlace.Local(wId),
                        new MirRvalue.MakeErrWrapper(resType.Err,
                            AsSwitchPatternSelection(isPat.Pattern, new MirOperand.CopyOrMove(MirPlace.Local(errBindingId), resType.Err, span))),
                        span),
                    span,
                    orderedBlockIds);
            }

            if (_module.IsLinearResource(resType) && (okExtraPat is not null || errExtraPat is not null))
            {
                EmitDiag(
                    VibraniumDiagnosticIds.LinearResourceViolation,
                    "Nested subpatterns inside linear Result<T, E> are banned in Vibranium.",
                    span);
            }

            int collapsedSubFailTarget = collapsed && collapsedAfterBothBranch is not null
                ? ResolveChainedBranchTargetWithLeavingRegions(falseBranch, collapsedAfterBothBranch, span, orderedBlockIds)
                : -1;
            if (okExtraPat is not null)
            {
                int okSubFail = collapsedSubFailTarget >= 0 ? collapsedSubFailTarget : errTarget;
                okTarget = IsRejectedLinearSwitchSubpattern(isPat.Pattern, resType)
                    ? okSubFail
                    : BuildExtraSubpatternCheckBlock(okBindingId, resType.Ok, consumedByVal, okExtraPat, okTarget, okSubFail, span, orderedBlockIds);
            }
            if (errExtraPat is not null)
            {
                int errSubFail = collapsedSubFailTarget >= 0 ? collapsedSubFailTarget : okTarget;
                errTarget = IsRejectedLinearSwitchSubpattern(isPat.Pattern, resType)
                    ? errSubFail
                    : BuildExtraSubpatternCheckBlock(errBindingId, resType.Err, consumedByVal, errExtraPat, errTarget, errSubFail, span, orderedBlockIds);
            }

            terminator = new MirTerminator.MatchResult(scrutinee, okBindingId, okTarget, errBindingId, errTarget, span);
            return true;
        }

        if (scrutMirType is MirType.OptionType or MirType.OptionRefType or MirType.OptionMutType or MirType.ResultType &&
            ContainsUnionPattern(isPat.Pattern))
        {
            var scrutinee = LowerMatchScrutinee(isPat.Value, scrutMirType, borrowMultiUseValueTemp: true);

            // An rvalue scrutinee is consumed by a negated single-arm pattern exactly as by the positive form
            // (SPEC 3.4): `Make() is not Some<T>(var v)` tests the value once, so peel the negations (swapping the
            // targets) and match the hidden temporary by value; its bindings then own the payload, a linear one
            // included. Everything else that reaches this path, logical patterns in particular (they test the
            // value more than once), keeps borrowing it in place, with its linear bans, as before.
            IPatternOperation checkedPattern = isPat.Pattern;
            int negations = 0;
            while (checkedPattern is INegatedPatternOperation negated)
            {
                checkedPattern = negated.Pattern;
                negations++;
            }
            bool consumedByVal = negations > 0 &&
                checkedPattern is not IBinaryPatternOperation &&
                scrutinee is MirMatchScrutinee.RvalueScrutinee { IsViewCursor: false };
            bool swapTargets = consumedByVal && negations % 2 == 1;
            if (!consumedByVal)
            {
                checkedPattern = isPat.Pattern;
            }

            if (_module.IsLinearResource(scrutMirType) && !consumedByVal)
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

            int trueTarget = ResolveBranchTargetWithLeavingRegions(trueBranch, span, orderedBlockIds);
            int falseTarget = ResolveBranchTargetWithLeavingRegions(falseBranch, span, orderedBlockIds);
            if (swapTargets)
            {
                (trueTarget, falseTarget) = (falseTarget, trueTarget);
            }
            terminator = BuildPatternCheckTerminator(
                scrutPlace,
                scrutMirType,
                scrutIsConsumedByValue: consumedByVal,
                checkedPattern,
                trueTarget,
                falseTarget,
                span,
                orderedBlockIds);
            return true;
        }

        return false;
    }

    private static SyntaxNode? FindSwitchForSelector(SyntaxNode value)
    {
        while (value.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            value = parentheses;
        }
        return value.Parent switch
        {
            SwitchStatementSyntax statement when statement.Expression == value => statement,
            SwitchExpressionSyntax expression when expression.GoverningExpression == value => expression,
            _ => null,
        };
    }

    private static IOperation UnwrapSwitchSelectorPlace(IOperation selector)
    {
        selector = UnwrapImplicitIdentityConversion(selector);
        while (selector is IParenthesizedOperation parentheses)
        {
            selector = UnwrapImplicitIdentityConversion(parentheses.Operand);
        }
        return selector;
    }

    private static bool IsPlaceThroughRefAssignment(IOperation value) => UnwrapSwitchSelectorPlace(value) switch
    {
        ISimpleAssignmentOperation { IsRef: true } => true,
        IFieldReferenceOperation { Instance: { } receiver } field when !IsWrapperFieldUnwrap(field) =>
            IsPlaceThroughRefAssignment(receiver),
        _ => false,
    };

    private MirPlace? RecoverSwitchMatchPlace(IOperation value, MirRvalue lowered, int firstStatement)
    {
        // Copy selectors can be snapshots. For a ref-returning indexer, reuse the address
        // used by that snapshot rather than evaluating its receiver or index a second time.
        if (IsSideEffectFreePlaceExpression(value))
        {
            return LowerPlace(value, isMutableAccess: false);
        }

        // A ref assignment yields the new place through the destination ref slot.
        // Keep that slot (not just its referent) in the match borrow, without re-evaluating the RHS.
        if ((!IsPlaceThroughRefAssignment(value) && !IsRefProducedOrProjectedPlaceExpression(value)) ||
            lowered is not MirRvalue.Use { Operand: MirOperand.CopyOrMove use })
        {
            return null;
        }
        if (use.Place.Projections.Count > 0 && use.Place.Projections[0] is MirProjection.Deref) return use.Place;
        if (_currentStmts.Count > firstStatement && _currentStmts[_currentStmts.Count - 1] is MirStatement.Assign
            { Target: { Projections.Count: 0 } temp, Rvalue: MirRvalue.Use { Operand: MirOperand.CopyOrMove read } } &&
            use.Place.Projections.Count == 0 && temp.LocalId == use.Place.LocalId &&
            read.Place.Projections.Count > 0 && read.Place.Projections[0] is MirProjection.Deref)
        {
            return read.Place;
        }
        return null;
    }

    private void BeginSwitchMatchBorrow(SyntaxNode switchSyntax, MirPlace place, MirType type, VibraniumSpan span)
    {
        // No generated native load or store: this reference only carries the shared match loan
        // in the checker. Capture the actual place after its address has been evaluated once.
        if (!_switchMatchBorrows.TryGetValue(switchSyntax, out int id))
        {
            id = CreateTempRefLocal(type, isMutable: false, span, "__switch_match", isSwitchMatchBorrow: true);
            _switchMatchBorrows.Add(switchSyntax, id);
        }
        _currentStmts.Add(new MirStatement.BindRef(id, IsMutable: false, place, span)
        {
            SwitchMatchRefSlots = FindSwitchMatchRefSlots(switchSyntax),
        });
    }

    private IReadOnlyList<int> FindSwitchMatchRefSlots(SyntaxNode switchSyntax)
    {
        // Walk the source selector, not the generated ref temporary or its provenance. In particular,
        // matching y after `ref readonly y = ref x` freezes y alone, not another alias of its referent.
        var selector = switchSyntax switch
        {
            SwitchStatementSyntax statement => statement.Expression,
            SwitchExpressionSyntax expression => expression.GoverningExpression,
            _ => throw new InvalidOperationException("Expected a switch selector."),
        };
        while (selector is ParenthesizedExpressionSyntax parentheses) selector = parentheses.Expression;
        var model = _compilation.GetSemanticModel(selector.SyntaxTree);
        var slots = new HashSet<int>();
        Visit(model.GetOperation(selector));
        return slots.OrderBy(slot => slot).ToArray();

        void Visit(IOperation? input)
        {
            if (input is null) return;
            switch (UnwrapSwitchSelectorPlace(input))
            {
                case ILocalReferenceOperation { Local.RefKind: not RefKind.None } local:
                    slots.Add(GetOrCreateLocalId(local.Local, isPatternBindingOverride: null));
                    break;
                case IFieldReferenceOperation field when !IsWrapperFieldUnwrap(field):
                    Visit(field.Instance);
                    break;
                // Index expressions locate the selected element, but do not become its access storage.
                case IPropertyReferenceOperation property when property.Property.ReturnsByRef ||
                    property.Property.ReturnsByRefReadonly || IsVecOrSpanIndexerAccess(property):
                    Visit(property.Instance);
                    break;
                case IImplicitIndexerReferenceOperation indexer when IsVecOrSpanIndexerAccess(indexer) ||
                    indexer.IndexerSymbol is IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true }:
                    Visit(indexer.Instance);
                    break;
                case IInvocationOperation call when call.TargetMethod.ReturnsByRef || call.TargetMethod.ReturnsByRefReadonly:
                    Visit(call.Instance);
                    foreach (var argument in call.Arguments)
                    {
                        if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.In or RefKind.RefReadOnlyParameter)
                        {
                            Visit(argument.Value);
                        }
                    }
                    break;
                case ISimpleAssignmentOperation { IsRef: true } assignment:
                    Visit(assignment.Target);
                    Visit(assignment.Value);
                    break;
            }
        }
    }

    private void KeepSwitchMatchBorrows(SyntaxNode? syntax)
    {
        if (syntax is null || _switchMatchBorrows.Count == 0) return;
        SyntaxNode? lastSwitch = null;
        for (SyntaxNode? node = syntax; node is not null; node = node.Parent)
        {
            // A guard's value is evaluated before the first successor statement on either edge.
            // An inner switch can be inside an outer guard, so check enclosing scopes too.
            var owner = node switch
            {
                WhenClauseSyntax { Parent: CasePatternSwitchLabelSyntax label } => label.Parent?.Parent as SwitchStatementSyntax,
                WhenClauseSyntax { Parent: SwitchExpressionArmSyntax arm } => arm.Parent as SwitchExpressionSyntax,
                PatternSyntax pattern => FindSwitchForPattern(pattern),
                _ => null,
            };
            if (owner is not null && owner != lastSwitch && _switchMatchBorrows.TryGetValue(owner, out int id))
            {
                _currentStmts.Add(new MirStatement.KeepAnalysisBorrow(id, VibraniumSymbolHelpers.ToVibraniumSpan(syntax)));
                lastSwitch = owner;
            }
        }
    }

    // The restriction was already diagnosed at the arm; do not analyze its detached body
    // or guard as if it could read designations the rejected subpattern never bound.
    private bool IsRejectedLinearSwitchSubpattern(IPatternOperation pattern, MirType type) =>
        _module.IsLinearResource(type) && FindSwitchForPattern(pattern.Syntax) is not null;

    private MirOperand AsSwitchPatternSelection(IPatternOperation pattern, MirOperand operand)
    {
        if (operand is MirOperand.CopyOrMove use && FindSwitchForPattern(pattern.Syntax) is { } owner &&
            _switchMatchBorrows.TryGetValue(owner, out int borrowId))
        {
            return use with { SelectingSwitchMatchBorrowId = borrowId };
        }
        return operand;
    }

    private MirMatchScrutinee ConfigureOptionSwitchPatternMatch(
        MirMatchScrutinee scrutinee, IPatternOperation testedPattern, IPatternOperation? somePattern,
        bool needsPayloadTest, ref int someBindingId)
    {
        if (scrutinee is not MirMatchScrutinee.PlaceScrutinee { IsMutableCursor: true } cursor ||
            FindSwitchForPattern(testedPattern.Syntax) is not { } owner)
        {
            return scrutinee;
        }

        // 2b validates the case syntax before MIR: only a wrapper designation or the
        // one positional payload can bind. Neither a tag nor a compiler temp binds.
        if (somePattern is null || !OptionCaseBindsPayload(somePattern))
        {
            if (!needsPayloadTest) someBindingId = -1;
            return cursor with { IsMutableCursor = false };
        }

        // Only the successful, unguarded binding ends its own match borrow. A failed pattern may
        // still reach a later arm, and a guard on this arm (or an enclosing match) still holds its loan.
        var arm = FindSwitchArmForPattern(somePattern.Syntax);
        bool isUnguarded = arm is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax { WhenClause: null } or
            SwitchExpressionArmSyntax { WhenClause: null };
        return isUnguarded && _switchMatchBorrows.TryGetValue(owner, out int borrowId)
            ? cursor with { SelectingSwitchMatchBorrowId = borrowId }
            : cursor;
    }

    private static bool OptionCaseBindsPayload(IPatternOperation somePattern) => somePattern switch
    {
        IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol } => true,
        IRecursivePatternOperation recursive => recursive.DeclaredSymbol is ILocalSymbol ||
            (recursive.DeconstructionSubpatterns.Length == 1 && recursive.DeconstructionSubpatterns[0].Syntax
                .DescendantNodesAndSelf().Any(node => node is SingleVariableDesignationSyntax)),
        _ => false,
    };

    private static SyntaxNode? FindSwitchForPattern(SyntaxNode patternSyntax) => FindSwitchArmForPattern(patternSyntax) switch
    {
        SwitchLabelSyntax label => label.Parent?.Parent as SwitchStatementSyntax,
        SwitchExpressionArmSyntax arm => arm.Parent as SwitchExpressionSyntax,
        _ => null,
    };

    private static SyntaxNode? FindSwitchArmForPattern(SyntaxNode patternSyntax)
    {
        // Parentheses and subpatterns are part of the arm's test, not its guard or result.
        SyntaxNode? pattern = patternSyntax;
        while (pattern?.Parent is PatternSyntax or SubpatternSyntax or PropertyPatternClauseSyntax or PositionalPatternClauseSyntax)
        {
            pattern = pattern.Parent;
        }
        // An unguarded statement case with a bare type name (such as NoneType) can
        // use the ambiguous value-label syntax even though Roslyn binds a type pattern.
        if (pattern is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax)
        {
            return pattern;
        }
        return pattern?.Parent switch
        {
            CaseSwitchLabelSyntax label when label.Value == pattern => label,
            CasePatternSwitchLabelSyntax label when label.Pattern == pattern => label,
            SwitchExpressionArmSyntax arm when arm.Pattern == pattern => arm,
            _ => null,
        };
    }

    // SPEC 2 bans a switch over a linear union rvalue whose arms test it again after an earlier arm consumed
    // it (LinearUnionSwitchBan: a guarded or repeated variant arm, or a 'not' / 'or' arm; VIB006 at the
    // arm). Its first read of the hidden scrutinee value consumes it as usual, so a genuine earlier move
    // of the scrutinee is still reported there; every later read takes a defined poison instead, so the
    // rejected switch does not also report a knock-on "use of moved value" for its own scrutinee.
    private bool IsBannedLinearUnionSwitch(SyntaxNode switchSyntax)
    {
        if (!_bannedLinearUnionSwitches.TryGetValue(switchSyntax, out bool banned))
        {
            var model = _compilation.GetSemanticModel(switchSyntax.SyntaxTree);
            banned = LinearUnionSwitchBan.Classify(switchSyntax, model).Violates;
            _bannedLinearUnionSwitches[switchSyntax] = banned;
        }

        return banned;
    }

    private MirPlace ReadBannedLinearUnionSwitchScrutinee(CaptureId captureId, int localId, MirType type, VibraniumSpan span)
    {
        if (!_bannedLinearUnionSwitchScrutineeRead[captureId])
        {
            _bannedLinearUnionSwitchScrutineeRead[captureId] = true;
            return MirPlace.Local(localId);
        }

        int poison = CreateTempLocal(type, span, "__banned_switch_reread");
        _currentStmts.Add(new MirStatement.Assign(
            MirPlace.Local(poison),
            new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(type), type, span)),
            span));
        return MirPlace.Local(poison);
    }

    private MirMatchScrutinee LowerMatchScrutinee(
        IOperation scrutOp,
        MirType scrutMirType,
        bool borrowMultiUseValueTemp = false)
    {
        var unwrapped = UnwrapImplicitIdentityConversion(scrutOp);

        bool isPlace = unwrapped switch
        {
            _ when unwrapped.ConstantValue.HasValue => false,
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IInstanceReferenceOperation => true,
            IFieldReferenceOperation fr => fr.Field.ContainingType is not null &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Some") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Ok") &&
                !VibraniumSymbolHelpers.IsVibraniumCoreType(fr.Field.ContainingType, "Err"),
            IFlowCaptureReferenceOperation fcr => _captures.TryGetValue(fcr.Id, out var c) &&
                (c.Kind == CaptureKind.DirectPlace ||
                 c.Kind == CaptureKind.ReservedPlace ||
                 c.Kind == CaptureKind.MultiBranchRef ||
                 (borrowMultiUseValueTemp &&
                  c.Kind == CaptureKind.ValueTemp &&
                  c.PatternUseCount > 1 &&
                  !_module.IsLinearResource(scrutMirType) &&
                  scrutMirType is not MirType.OptionRefType)),
            _ => false,
        };

        if (isPlace)
        {
            bool isMutCursor = scrutMirType is MirType.OptionMutType;
            var place = LowerPlace(unwrapped, isMutableAccess: scrutMirType is MirType.OptionMutType);
            return new MirMatchScrutinee.PlaceScrutinee(place, scrutMirType, isMutCursor);
        }

        bool isViewCursor = scrutMirType is MirType.OptionRefType or MirType.OptionMutType;
        var operand = LowerOperand(unwrapped, scrutMirType);
        return new MirMatchScrutinee.RvalueScrutinee(operand, isViewCursor);
    }

    private bool TryExtractOptionArm(
        IPatternOperation pattern,
        MirType innerType,
        VibraniumSpan span,
        out bool isSomeArm,
        out int someBindingLocalId,
        out ILocalSymbol? wrapperLocalSym,
        out IReadOnlyList<IPatternOperation>? extraSubpattern,
        bool allocateUnusedSome = true)
    {
        isSomeArm = false;
        someBindingLocalId = -1;
        wrapperLocalSym = null;
        extraSubpattern = null;

        ITypeSymbol? matchedType = pattern switch
        {
            IRecursivePatternOperation rp => SubstituteType(rp.MatchedType),
            IDeclarationPatternOperation dp => SubstituteType(dp.MatchedType),
            ITypePatternOperation tp => SubstituteType(tp.MatchedType),
            _ => null,
        };

        if (matchedType is null) return false;

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(matchedType, "NoneType"))
        {
            isSomeArm = false;
            wrapperLocalSym = pattern switch
            {
                IDeclarationPatternOperation noneDecl => noneDecl.DeclaredSymbol as ILocalSymbol,
                IRecursivePatternOperation noneRec => noneRec.DeclaredSymbol as ILocalSymbol,
                _ => null,
            };
            someBindingLocalId = allocateUnusedSome ? CreatePatternLocal(innerType, span, "__some_unused") : -1;
            return true;
        }

        if (VibraniumSymbolHelpers.IsVibraniumCoreType(matchedType, "Some"))
        {
            isSomeArm = true;
            if (pattern is IRecursivePatternOperation recPat)
            {
                wrapperLocalSym = recPat.DeclaredSymbol as ILocalSymbol;
                someBindingLocalId = ExtractUnionPayloadPattern(recPat, innerType, span, out extraSubpattern);
                return true;
            }

            if (pattern is IDeclarationPatternOperation declPat)
            {
                wrapperLocalSym = declPat.DeclaredSymbol as ILocalSymbol;
                someBindingLocalId = CreatePatternLocal(innerType, span, "__some_val");
                return true;
            }

            if (pattern is ITypePatternOperation)
            {
                someBindingLocalId = CreatePatternLocal(innerType, span, "__some_val");
                return true;
            }
        }

        return false;
    }

    // A NoneType designation holds the unit value.
    private MirStatement NoneDesignationAssign(ILocalSymbol sym, VibraniumSpan span, bool? isPatternBindingOverride) =>
        new MirStatement.Assign(
            MirPlace.Local(GetOrCreateLocalId(sym, isPatternBindingOverride)),
            new MirRvalue.Use(new MirOperand.Const(new MirConstant.ZeroDefault(MirType.NoneUnit), MirType.NoneUnit, span)),
            span);

    private bool TryExtractResultArm(
        IPatternOperation pattern,
        MirType.ResultType resType,
        VibraniumSpan span,
        out bool isOkArm,
        out int bindingLocalId,
        out ILocalSymbol? wrapperLocalSym,
        out IReadOnlyList<IPatternOperation>? extraSubpattern)
    {
        isOkArm = false;
        bindingLocalId = -1;
        wrapperLocalSym = null;
        extraSubpattern = null;

        ITypeSymbol? matchedType = pattern switch
        {
            IRecursivePatternOperation rp => SubstituteType(rp.MatchedType),
            IDeclarationPatternOperation dp => SubstituteType(dp.MatchedType),
            ITypePatternOperation tp => SubstituteType(tp.MatchedType),
            _ => null,
        };

        if (matchedType is null) return false;

        bool matchOk = VibraniumSymbolHelpers.IsVibraniumCoreType(matchedType, "Ok");
        bool matchErr = VibraniumSymbolHelpers.IsVibraniumCoreType(matchedType, "Err");
        if (!matchOk && !matchErr) return false;

        isOkArm = matchOk;
        var payloadType = matchOk ? resType.Ok : resType.Err;

        if (pattern is IRecursivePatternOperation recPat)
        {
            wrapperLocalSym = recPat.DeclaredSymbol as ILocalSymbol;
            bindingLocalId = ExtractUnionPayloadPattern(recPat, payloadType, span, out extraSubpattern);
            return true;
        }

        if (pattern is IDeclarationPatternOperation declPat)
        {
            wrapperLocalSym = declPat.DeclaredSymbol as ILocalSymbol;
            bindingLocalId = CreatePatternLocal(payloadType, span, "__res_val");
            return true;
        }

        if (pattern is ITypePatternOperation)
        {
            bindingLocalId = CreatePatternLocal(payloadType, span, "__res_val");
            return true;
        }

        return false;
    }

    private bool IsDirectPayloadBinding(IPatternOperation pattern, out ILocalSymbol local)
    {
        if (pattern is IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol symbol } declaration &&
            (declaration.MatchedType is null || SymbolEqualityComparer.Default.Equals(SubstituteType(declaration.MatchedType), SubstituteType(declaration.InputType))))
        {
            local = symbol;
            return true;
        }
        local = null!;
        return false;
    }

    // Recovery treats the underlying test as unmatched. A leading `not`
    // inverts that result; its Boolean-false edge may require bindings from
    // the invalid pattern (for example, `if (o is not Some<T>(var x)) return;`).
    private static bool RejectedPatternResult(IPatternOperation pattern)
    {
        bool matched = false;
        while (pattern is INegatedPatternOperation negated)
        {
            matched = !matched;
            pattern = negated.Pattern;
        }
        return matched;
    }

    private bool RejectUnsupportedPattern(IPatternOperation pattern)
    {
        // Preserve VIB008 even when the same pattern also contains a cut.
        bool hasNullConstant = RejectNullConstantPatterns(pattern);
        return _declarationCuts.RejectsUnionPattern(pattern) || hasNullConstant || RejectInvalidUnionDeconstruction(pattern);
    }

    private bool RejectNullConstantPatterns(IPatternOperation pattern)
    {
        bool rejected = false;
        foreach (var constant in pattern.DescendantsAndSelf().OfType<IConstantPatternOperation>())
        {
            if (constant.Value.ConstantValue is not { HasValue: true, Value: null } ||
                (constant.Value.Type?.IsReferenceType != true && constant.InputType.IsReferenceType != true)) continue;
            bool literal = constant.Value.Syntax.IsKind(SyntaxKind.NullLiteralExpression);
            EmitDiag(VibraniumDiagnosticIds.NullOrZeroUnsafe,
                literal ? "Null literals are banned in Vibranium; use Option<T> (Some/None)." :
                    "Null constant patterns are banned in Vibranium; use Option<T> (Some/None).",
                VibraniumSymbolHelpers.ToVibraniumSpan(constant.Value.Syntax));
            rejected = true;
        }
        return rejected;
    }

    private bool RejectInvalidUnionDeconstruction(IPatternOperation pattern)
    {
        bool rejected = false;
        foreach (var recursive in pattern.DescendantsAndSelf().OfType<IRecursivePatternOperation>())
        {
            var wrapper = SubstituteType(recursive.MatchedType);
            if (!IsUnionArmType(wrapper)) continue;
            bool none = VibraniumSymbolHelpers.IsVibraniumCoreType(wrapper, "NoneType");
            bool hasDeconstruction = recursive.DeconstructSymbol is not null || !recursive.DeconstructionSubpatterns.IsEmpty;
            bool invalid = none ? hasDeconstruction :
                hasDeconstruction && !(recursive.DeconstructionSubpatterns.Length == 1 && recursive.DeconstructSymbol is IMethodSymbol method &&
                    !method.IsExtensionMethod && SymbolEqualityComparer.Default.Equals(SubstituteType(method.ContainingType), wrapper));
            if (!invalid) continue;
            EmitDiag(VibraniumDiagnosticIds.Unsupported, none
                ? "Payload subpatterns on NoneType are not supported in Vibranium v0.2; use NoneType without a payload subpattern."
                : "Custom union-case deconstruction is not supported in Vibranium v0.2; use the wrapper's single positional payload.",
                VibraniumSymbolHelpers.ToVibraniumSpan(recursive.Syntax));
            rejected = true;
        }
        return rejected;
    }

    private int ExtractUnionPayloadPattern(
        IRecursivePatternOperation pattern,
        MirType payloadType,
        VibraniumSpan span,
        out IReadOnlyList<IPatternOperation>? extraSubpatterns)
    {
        // Syntax validation rejects property clauses; invalid deconstruction
        // has already taken the failure edge. Only the wrapper's one positional
        // subpattern can inspect or bind the extracted payload.
        extraSubpatterns = null;
        if (pattern.DeconstructionSubpatterns.Length == 1)
        {
            var subpattern = pattern.DeconstructionSubpatterns[0];
            if (IsDirectPayloadBinding(subpattern, out var local))
            {
                return GetOrCreateLocalId(local, isPatternBindingOverride: null);
            }
            if (subpattern is IDiscardPatternOperation)
            {
                if (_module.IsLinearResource(payloadType))
                {
                    EmitDiag(VibraniumDiagnosticIds.LinearResourceViolation, "Cannot discard linear IDisposable resource with '_' pattern.",
                        VibraniumSymbolHelpers.ToVibraniumSpan(subpattern.Syntax));
                }
            }
            else
            {
                extraSubpatterns = new[] { subpattern };
            }
        }
        return CreatePatternLocal(payloadType, span, "__union_payload");
    }

    private void MarkOwnedPatternBindingIfConsumed(int localId, bool consumedByValue)
    {
        if (consumedByValue &&
            localId >= 0 &&
            localId < _locals.Count &&
            !_locals[localId].IsCompilerTemp &&
            _locals[localId].IsPatternBinding)
        {
            _locals[localId] = _locals[localId] with { IsPatternBinding = false };
        }
    }

    private static bool CaptureReferencedBeforeRedef(
        ControlFlowGraph cfg,
        IOperation scrutineeOp,
        ControlFlowBranch startBranch)
    {
        if (UnwrapImplicitIdentityConversion(scrutineeOp) is not IFlowCaptureReferenceOperation fcr)
        {
            return true;
        }
        if (startBranch.Destination is null || IsImplicitSwitchExhaustiveFallback(startBranch))
        {
            return false;
        }

        var capId = fcr.Id;
        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        if (visited.Add(startBranch.Destination.Ordinal))
        {
            queue.Enqueue(startBranch.Destination.Ordinal);
        }

        while (queue.Count > 0)
        {
            int ord = queue.Dequeue();
            if (ord < 0 || ord >= cfg.Blocks.Length)
            {
                continue;
            }
            var blk = cfg.Blocks[ord];
            if (IsInsideFinallyOrCatchRegion(blk.EnclosingRegion))
            {
                continue;
            }

            bool redefined = false;
            foreach (var op in blk.Operations)
            {
                foreach (var sub in op.DescendantsAndSelf())
                {
                    if (sub is IFlowCaptureReferenceOperation refOp && refOp.Id.Equals(capId))
                    {
                        return true;
                    }
                    if (sub is IFlowCaptureOperation defOp && defOp.Id.Equals(capId))
                    {
                        redefined = true;
                        break;
                    }
                }
                if (redefined)
                {
                    break;
                }
            }
            if (redefined)
            {
                continue;
            }

            if (blk.BranchValue is not null)
            {
                if (blk.BranchValue is IIsPatternOperation { Pattern: IDiscardPatternOperation } dPat &&
                    UnwrapImplicitIdentityConversion(dPat.Value) is IFlowCaptureReferenceOperation dFcr &&
                    dFcr.Id.Equals(capId))
                {
                    var onlyTrue = blk.ConditionKind == ControlFlowConditionKind.WhenTrue
                        ? blk.ConditionalSuccessor
                        : blk.FallThroughSuccessor;
                    if (onlyTrue?.Destination is { } td && visited.Add(td.Ordinal))
                    {
                        queue.Enqueue(td.Ordinal);
                    }
                    continue;
                }

                foreach (var sub in blk.BranchValue.DescendantsAndSelf())
                {
                    if (sub is IFlowCaptureReferenceOperation refOp && refOp.Id.Equals(capId))
                    {
                        return true;
                    }
                    if (sub is IFlowCaptureOperation defOp && defOp.Id.Equals(capId))
                    {
                        redefined = true;
                        break;
                    }
                }
                if (redefined)
                {
                    continue;
                }
            }

            if (blk.FallThroughSuccessor?.Destination is { } fd && visited.Add(fd.Ordinal))
            {
                queue.Enqueue(fd.Ordinal);
            }
            if (blk.ConditionalSuccessor?.Destination is { } cd && visited.Add(cd.Ordinal))
            {
                queue.Enqueue(cd.Ordinal);
            }
        }

        return false;
    }

    private bool TryCollapseComplementaryOptionArm(
        ControlFlowGraph cfg,
        IOperation firstScrutineeOp,
        ControlFlowBranch falseBranchOfFirst,
        bool expectSome,
        bool arm1HasExtraSub,
        MirType innerType,
        VibraniumSpan span,
        out ControlFlowBranch collapsedTargetBranch,
        out int arm2SomeBindingId,
        out ILocalSymbol? arm2WrapperSym,
        out IReadOnlyList<IPatternOperation>? arm2ExtraSub,
        out ControlFlowBranch afterSecondArmBranch)
    {
        collapsedTargetBranch = null!;
        arm2SomeBindingId = -1;
        arm2WrapperSym = null;
        arm2ExtraSub = null;
        afterSecondArmBranch = null!;

        var nextBlock = falseBranchOfFirst.Destination;
        if (nextBlock is null || nextBlock.Operations.Length > 0 || HasAnyUsingLocals(falseBranchOfFirst.LeavingRegions))
        {
            return false;
        }
        if (nextBlock.ConditionKind == ControlFlowConditionKind.None || nextBlock.BranchValue is not IIsPatternOperation nextPat)
        {
            return false;
        }
        if (HasSwitchCaseWhenGuard(nextPat) || !SameFlowCaptureScrutinee(firstScrutineeOp, nextPat.Value))
        {
            return false;
        }
        if (RejectUnsupportedPattern(nextPat.Pattern) ||
            !TryExtractOptionArm(nextPat.Pattern, innerType, span, out bool nextIsSome, out int nextSomeId, out var nextWrapper, out var extraSub, allocateUnusedSome: expectSome) ||
            nextIsSome != expectSome)
        {
            return false;
        }

        var nextTrue = nextBlock.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? nextBlock.ConditionalSuccessor!
            : nextBlock.FallThroughSuccessor!;
        var nextFalse = nextBlock.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? nextBlock.FallThroughSuccessor!
            : nextBlock.ConditionalSuccessor!;

        bool exhaustiveDirectPair =
            !arm1HasExtraSub &&
            extraSub is null &&
            (IsUnconditionalSwitchStatementCase(nextPat) || IsImplicitSwitchExhaustiveFallback(nextFalse));
        if (!exhaustiveDirectPair && CaptureReferencedBeforeRedef(cfg, firstScrutineeOp, nextFalse))
        {
            return false;
        }

        _collapsedCfgBlockOrdinals.Add(nextBlock.Ordinal);
        collapsedTargetBranch = nextTrue;
        arm2SomeBindingId = nextSomeId;
        arm2WrapperSym = nextWrapper;
        arm2ExtraSub = extraSub;
        afterSecondArmBranch = nextFalse;
        return true;
    }

    private bool TryCollapseComplementaryResultArm(
        ControlFlowGraph cfg,
        IOperation firstScrutineeOp,
        ControlFlowBranch falseBranchOfFirst,
        bool expectOk,
        bool arm1HasExtraSub,
        MirType.ResultType resType,
        VibraniumSpan span,
        out ControlFlowBranch collapsedTargetBranch,
        out int arm2BindingId,
        out ILocalSymbol? arm2WrapperSym,
        out IReadOnlyList<IPatternOperation>? arm2ExtraSub,
        out ControlFlowBranch afterSecondArmBranch)
    {
        collapsedTargetBranch = null!;
        arm2BindingId = -1;
        arm2WrapperSym = null;
        arm2ExtraSub = null;
        afterSecondArmBranch = null!;

        var nextBlock = falseBranchOfFirst.Destination;
        if (nextBlock is null || nextBlock.Operations.Length > 0 || HasAnyUsingLocals(falseBranchOfFirst.LeavingRegions))
        {
            return false;
        }
        if (nextBlock.ConditionKind == ControlFlowConditionKind.None || nextBlock.BranchValue is not IIsPatternOperation nextPat)
        {
            return false;
        }
        if (HasSwitchCaseWhenGuard(nextPat) || !SameFlowCaptureScrutinee(firstScrutineeOp, nextPat.Value))
        {
            return false;
        }
        if (RejectUnsupportedPattern(nextPat.Pattern) ||
            !TryExtractResultArm(nextPat.Pattern, resType, span, out bool nextIsOk, out int nextBindId, out var nextWrapper, out var extraSub) ||
            nextIsOk != expectOk)
        {
            return false;
        }

        var nextTrue = nextBlock.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? nextBlock.ConditionalSuccessor!
            : nextBlock.FallThroughSuccessor!;
        var nextFalse = nextBlock.ConditionKind == ControlFlowConditionKind.WhenTrue
            ? nextBlock.FallThroughSuccessor!
            : nextBlock.ConditionalSuccessor!;

        bool exhaustiveDirectPair =
            !arm1HasExtraSub &&
            extraSub is null &&
            (IsUnconditionalSwitchStatementCase(nextPat) || IsImplicitSwitchExhaustiveFallback(nextFalse));
        if (!exhaustiveDirectPair && CaptureReferencedBeforeRedef(cfg, firstScrutineeOp, nextFalse))
        {
            return false;
        }

        _collapsedCfgBlockOrdinals.Add(nextBlock.Ordinal);
        collapsedTargetBranch = nextTrue;
        arm2BindingId = nextBindId;
        arm2WrapperSym = nextWrapper;
        arm2ExtraSub = extraSub;
        afterSecondArmBranch = nextFalse;
        return true;
    }

    private static bool SameFlowCaptureScrutinee(IOperation a, IOperation b)
    {
        a = UnwrapImplicitIdentityConversion(a);
        b = UnwrapImplicitIdentityConversion(b);
        return a is IFlowCaptureReferenceOperation fa &&
               b is IFlowCaptureReferenceOperation fb &&
               fa.Id.Equals(fb.Id);
    }

    // A guarded case label or switch expression arm: its guard can fail after the pattern matched, and the next arm
    // then tests the scrutinee again, so the test must not be collapsed with a complementary next arm.
    private static bool HasSwitchCaseWhenGuard(IIsPatternOperation patOp) =>
        patOp.Syntax is CasePatternSwitchLabelSyntax { WhenClause: not null } or SwitchExpressionArmSyntax { WhenClause: not null } ||
        patOp.Syntax?.Parent is CasePatternSwitchLabelSyntax { WhenClause: not null } or SwitchExpressionArmSyntax { WhenClause: not null };

    private static bool IsUnconditionalSwitchStatementCase(IIsPatternOperation patOp) =>
        patOp.Syntax is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax { WhenClause: null } ||
        patOp.Syntax?.Parent is CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax { WhenClause: null };

    private static bool IsNaNConstantOperation(IOperation op)
    {
        if (op.ConstantValue is { HasValue: true, Value: var cv })
        {
            return (cv is double d && double.IsNaN(d)) || (cv is float f && float.IsNaN(f));
        }

        if (UnwrapImplicitIdentityConversion(op) is IFieldReferenceOperation
            {
                Field: { IsConst: true, HasConstantValue: true, ConstantValue: var fcv },
            })
        {
            return (fcv is double fd && double.IsNaN(fd)) || (fcv is float ff && float.IsNaN(ff));
        }

        return false;
    }

    private bool TryExtractSwitchCaseNaNScrutinee(
        IBinaryOperation bin,
        out IOperation scrutineeOp,
        out MirType scrutineeMirType)
    {
        scrutineeOp = null!;
        scrutineeMirType = MirType.Void;

        if (!bin.IsImplicit || bin.OperatorKind != BinaryOperatorKind.Equals || bin.Syntax is null)
        {
            return false;
        }

        var caseLabel = bin.Syntax as CaseSwitchLabelSyntax
            ?? bin.Syntax.Ancestors().OfType<CaseSwitchLabelSyntax>().FirstOrDefault();
        if (caseLabel is null)
        {
            return false;
        }

        var leftMirType = MapMirType(SubstituteType(bin.LeftOperand.Type));
        var rightMirType = MapMirType(SubstituteType(bin.RightOperand.Type));
        if (leftMirType is not (MirType.Float32Type or MirType.Float64Type) ||
            rightMirType is not (MirType.Float32Type or MirType.Float64Type))
        {
            return false;
        }

        bool rightInLabelValue = bin.RightOperand.Syntax is not null &&
            caseLabel.Value.Span.Contains(bin.RightOperand.Syntax.Span);
        bool leftInLabelValue = bin.LeftOperand.Syntax is not null &&
            caseLabel.Value.Span.Contains(bin.LeftOperand.Syntax.Span);

        if (rightInLabelValue && !leftInLabelValue)
        {
            if (!IsNaNConstantOperation(bin.RightOperand))
            {
                return false;
            }
            scrutineeOp = bin.LeftOperand;
            scrutineeMirType = leftMirType;
            return true;
        }

        if (leftInLabelValue && !rightInLabelValue)
        {
            if (!IsNaNConstantOperation(bin.LeftOperand))
            {
                return false;
            }
            scrutineeOp = bin.RightOperand;
            scrutineeMirType = rightMirType;
            return true;
        }

        if (IsNaNConstantOperation(bin.RightOperand) &&
            UnwrapImplicitIdentityConversion(bin.LeftOperand) is IFlowCaptureReferenceOperation)
        {
            scrutineeOp = bin.LeftOperand;
            scrutineeMirType = leftMirType;
            return true;
        }

        if (IsNaNConstantOperation(bin.LeftOperand) &&
            UnwrapImplicitIdentityConversion(bin.RightOperand) is IFlowCaptureReferenceOperation)
        {
            scrutineeOp = bin.RightOperand;
            scrutineeMirType = rightMirType;
            return true;
        }

        return false;
    }

    private static bool IsImplicitSwitchExhaustiveFallback(ControlFlowBranch branch)
    {
        var dest = branch.Destination;
        if (dest is null) return true;
        if (dest.Operations.Length == 0 &&
            dest.ConditionKind == ControlFlowConditionKind.None &&
            dest.FallThroughSuccessor?.Destination is null)
        {
            return true;
        }
        return false;
    }

    private int PrependStatementViaTrampoline(
        int targetBlockId,
        MirStatement stmt,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        int trampId = _nextSyntheticBlockId++;
        orderedBlockIds.Add(trampId);
        _mirBlockStmts[trampId] = new List<MirStatement> { stmt };
        _mirBlockTerms[trampId] = new MirTerminator.Goto(targetBlockId, span);
        return trampId;
    }

    private bool ContainsUnionPattern(IPatternOperation pattern) =>
        pattern switch
        {
            IRecursivePatternOperation rp => IsUnionArmType(SubstituteType(rp.MatchedType)),
            IDeclarationPatternOperation dp => IsUnionArmType(SubstituteType(dp.MatchedType)),
            ITypePatternOperation tp => IsUnionArmType(SubstituteType(tp.MatchedType)),
            INegatedPatternOperation np => ContainsUnionPattern(np.Pattern),
            IBinaryPatternOperation bp => ContainsUnionPattern(bp.LeftPattern) || ContainsUnionPattern(bp.RightPattern),
            _ => false,
        };

    internal static bool IsUnionArmType(ITypeSymbol? type) =>
        type is not null &&
        (VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Some") ||
         VibraniumSymbolHelpers.IsVibraniumCoreType(type, "NoneType") ||
         VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Ok") ||
         VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Err"));

    // All subpatterns inspect the same payload; each has the same failure continuation.
    private int BuildExtraSubpatternCheckBlock(
        int payloadLocalId,
        MirType payloadType,
        bool payloadIsConsumedByValue,
        IReadOnlyList<IPatternOperation> subpatterns,
        int matchTarget,
        int failTarget,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        var place = MirPlace.Local(payloadLocalId);
        for (int index = subpatterns.Count - 1; index >= 0; index--)
        {
            matchTarget = BuildPatternCheckBlock(place, payloadType, payloadIsConsumedByValue,
                subpatterns[index], matchTarget, failTarget, span, orderedBlockIds);
        }
        return matchTarget;
    }

    private int BuildPatternCheckBlock(
        MirPlace scrutPlace,
        MirType scrutType,
        bool scrutIsConsumedByValue,
        IPatternOperation pattern,
        int matchTarget,
        int failTarget,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        int checkBlockId = _nextSyntheticBlockId++;
        orderedBlockIds.Add(checkBlockId);

        int savedBlockId = _currentBlockId;
        var savedStmts = _currentStmts;
        _currentBlockId = checkBlockId;
        _currentStmts = new List<MirStatement>();

        var term = BuildPatternCheckTerminator(
            scrutPlace,
            scrutType,
            scrutIsConsumedByValue,
            pattern,
            matchTarget,
            failTarget,
            span,
            orderedBlockIds);

        _mirBlockStmts[_currentBlockId] = _currentStmts;
        _mirBlockTerms[_currentBlockId] = term;
        _currentBlockId = savedBlockId;
        _currentStmts = savedStmts;
        return checkBlockId;
    }

    private MirTerminator BuildPatternCheckTerminator(
        MirPlace scrutPlace,
        MirType scrutType,
        bool scrutIsConsumedByValue,
        IPatternOperation pattern,
        int matchTarget,
        int failTarget,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        if (RejectUnsupportedPattern(pattern))
            return new MirTerminator.Goto(RejectedPatternResult(pattern) ? matchTarget : failTarget, span);

        switch (pattern)
        {
            case INegatedPatternOperation np:
                if (_module.IsLinearResource(scrutType))
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"Negated pattern '{pattern.Syntax}' cannot be used on linear resource '{scrutType}'.",
                        span);
                }
                return BuildPatternCheckTerminator(
                    scrutPlace,
                    scrutType,
                    scrutIsConsumedByValue: false,
                    np.Pattern,
                    failTarget,
                    matchTarget,
                    span,
                    orderedBlockIds);

            case IBinaryPatternOperation bp:
            {
                if (_module.IsLinearResource(scrutType))
                {
                    EmitDiag(
                        VibraniumDiagnosticIds.LinearResourceViolation,
                        $"Logical pattern '{pattern.Syntax}' cannot be used on linear resource '{scrutType}'.",
                        span);
                }
                int rightBlock = BuildPatternCheckBlock(
                    scrutPlace,
                    scrutType,
                    scrutIsConsumedByValue: false,
                    bp.RightPattern,
                    matchTarget,
                    failTarget,
                    span,
                    orderedBlockIds);
                return bp.OperatorKind == BinaryOperatorKind.Or
                    ? BuildPatternCheckTerminator(
                        scrutPlace,
                        scrutType,
                        scrutIsConsumedByValue: false,
                        bp.LeftPattern,
                        matchTarget,
                        rightBlock,
                        span,
                        orderedBlockIds)
                    : BuildPatternCheckTerminator(
                        scrutPlace,
                        scrutType,
                        scrutIsConsumedByValue: false,
                        bp.LeftPattern,
                        rightBlock,
                        failTarget,
                        span,
                        orderedBlockIds);
            }
        }

        if (scrutType is MirType.OptionType or MirType.OptionRefType or MirType.OptionMutType)
        {
            MirType innerType = scrutType switch
            {
                MirType.OptionType o => o.Inner,
                MirType.OptionRefType orf => orf.Inner,
                MirType.OptionMutType om => om.Inner,
                _ => MirType.Int32,
            };

            if (TryExtractOptionArm(pattern, innerType, span, out bool isSomeArm, out int someBindingId, out var wrapperLocalSym, out var extraSubpattern))
            {
                if (_module.IsLinearResource(scrutType))
                {
                    if (!scrutIsConsumedByValue)
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

                bool isView = scrutType is MirType.OptionRefType or MirType.OptionMutType;
                bool subConsumedByVal = scrutIsConsumedByValue && !isView;
                MarkOwnedPatternBindingIfConsumed(someBindingId, subConsumedByVal);
                MirMatchScrutinee subScrut = scrutIsConsumedByValue
                    ? new MirMatchScrutinee.RvalueScrutinee(new MirOperand.CopyOrMove(scrutPlace, scrutType, span), isView)
                    : new MirMatchScrutinee.PlaceScrutinee(scrutPlace, scrutType, IsMutableCursor: scrutType is MirType.OptionMutType);
                subScrut = ConfigureOptionSwitchPatternMatch(subScrut, pattern, isSomeArm ? pattern : null,
                    extraSubpattern is not null, ref someBindingId);

                int someTarget = isSomeArm ? matchTarget : failTarget;
                int noneTarget = isSomeArm ? failTarget : matchTarget;

                if (isSomeArm && wrapperLocalSym is not null)
                {
                    int wId = GetOrCreateLocalId(wrapperLocalSym, isPatternBindingOverride: null);
                    MarkOwnedPatternBindingIfConsumed(wId, subConsumedByVal);
                    someTarget = PrependStatementViaTrampoline(
                        someTarget,
                        new MirStatement.Assign(
                            MirPlace.Local(wId),
                            new MirRvalue.MakeSomeWrapper(innerType,
                                AsSwitchPatternSelection(pattern, new MirOperand.CopyOrMove(MirPlace.Local(someBindingId), innerType, span))),
                            span),
                        span,
                        orderedBlockIds);
                }

                if (!isSomeArm && wrapperLocalSym is not null)
                {
                    noneTarget = PrependStatementViaTrampoline(noneTarget, NoneDesignationAssign(wrapperLocalSym, span, null), span, orderedBlockIds);
                }

                if (isSomeArm && extraSubpattern is not null)
                {
                    someTarget = BuildExtraSubpatternCheckBlock(
                        someBindingId,
                        innerType,
                        scrutIsConsumedByValue && !isView,
                        extraSubpattern,
                        someTarget,
                        failTarget,
                        span,
                        orderedBlockIds);
                }

                return new MirTerminator.MatchOption(subScrut, someBindingId, someTarget, noneTarget, span);
            }
        }
        else if (scrutType is MirType.ResultType resType &&
                 TryExtractResultArm(pattern, resType, span, out bool isOkArm, out int bindingId, out var resWrapperSym, out var resExtraSub))
        {
            if (_module.IsLinearResource(resType))
            {
                if (!scrutIsConsumedByValue)
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

            MirMatchScrutinee subScrut = scrutIsConsumedByValue
                ? new MirMatchScrutinee.RvalueScrutinee(new MirOperand.CopyOrMove(scrutPlace, resType, span), IsViewCursor: false)
                : new MirMatchScrutinee.PlaceScrutinee(scrutPlace, resType, IsMutableCursor: false);

            int okBindingId = isOkArm ? bindingId : CreatePatternLocal(resType.Ok, span, "__ok_unused");
            int errBindingId = isOkArm ? CreatePatternLocal(resType.Err, span, "__err_unused") : bindingId;
            MarkOwnedPatternBindingIfConsumed(okBindingId, scrutIsConsumedByValue);
            MarkOwnedPatternBindingIfConsumed(errBindingId, scrutIsConsumedByValue);
            int okTarget = isOkArm ? matchTarget : failTarget;
            int errTarget = isOkArm ? failTarget : matchTarget;

            if (resWrapperSym is not null)
            {
                int wId = GetOrCreateLocalId(resWrapperSym, isPatternBindingOverride: null);
                MarkOwnedPatternBindingIfConsumed(wId, scrutIsConsumedByValue);
                if (isOkArm)
                {
                    okTarget = PrependStatementViaTrampoline(
                        okTarget,
                        new MirStatement.Assign(
                            MirPlace.Local(wId),
                            new MirRvalue.MakeOkWrapper(resType.Ok,
                                AsSwitchPatternSelection(pattern, new MirOperand.CopyOrMove(MirPlace.Local(okBindingId), resType.Ok, span))),
                            span),
                        span,
                        orderedBlockIds);
                }
                else
                {
                    errTarget = PrependStatementViaTrampoline(
                        errTarget,
                        new MirStatement.Assign(
                            MirPlace.Local(wId),
                            new MirRvalue.MakeErrWrapper(resType.Err,
                                AsSwitchPatternSelection(pattern, new MirOperand.CopyOrMove(MirPlace.Local(errBindingId), resType.Err, span))),
                            span),
                        span,
                        orderedBlockIds);
                }
            }

            if (resExtraSub is not null)
            {
                if (isOkArm)
                {
                    okTarget = BuildExtraSubpatternCheckBlock(
                        okBindingId,
                        resType.Ok,
                        scrutIsConsumedByValue,
                        resExtraSub,
                        okTarget,
                        failTarget,
                        span,
                        orderedBlockIds);
                }
                else
                {
                    errTarget = BuildExtraSubpatternCheckBlock(
                        errBindingId,
                        resType.Err,
                        scrutIsConsumedByValue,
                        resExtraSub,
                        errTarget,
                        failTarget,
                        span,
                        orderedBlockIds);
                }
            }

            return new MirTerminator.MatchResult(subScrut, okBindingId, okTarget, errBindingId, errTarget, span);
        }

        if (pattern is IDeclarationPatternOperation declaration &&
            declaration.MatchedType is not null &&
            !SymbolEqualityComparer.Default.Equals(SubstituteType(declaration.MatchedType), SubstituteType(declaration.InputType)))
        {
            // Type-changing declarations cannot introduce a bound payload on
            // the success edge. Reject before lowering any reference to it.
            EmitDiag(VibraniumDiagnosticIds.Unsupported,
                $"Pattern '{pattern.Syntax}' is not supported in Vibranium.",
                VibraniumSymbolHelpers.ToVibraniumSpan(pattern.Syntax));
            return new MirTerminator.Goto(failTarget, span);
        }

        if (pattern is IRecursivePatternOperation or IListPatternOperation)
        {
            // Supported union patterns returned above. Unsupported structural patterns
            // must not add an analysis path into an uninitialized designation's scope.
            EmitDiag(VibraniumDiagnosticIds.Unsupported,
                "Structural payload patterns are not supported in Vibranium v0.2; bind the payload and test its members in a when guard or an if condition.",
                VibraniumSymbolHelpers.ToVibraniumSpan(pattern.Syntax));
            return new MirTerminator.Goto(failTarget, span);
        }
        var payloadOp = new MirOperand.CopyOrMove(scrutPlace, scrutType, span);
        var condOp = LowerNonUnionPatternTest(payloadOp, scrutType, pattern, span);
        if (scrutIsConsumedByValue &&
            pattern is IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declSubLoc } &&
            _localMap.TryGetValue(declSubLoc, out int declSubId))
        {
            MarkOwnedPatternBindingIfConsumed(declSubId, consumedByValue: true);
        }
        return new MirTerminator.Branch(condOp, matchTarget, failTarget, span);
    }

    private static bool HasAnyUsingLocals(ImmutableArray<ControlFlowRegion> leavingRegions)
    {
        foreach (var r in leavingRegions)
        {
            foreach (var l in r.Locals)
            {
                if (l.IsUsing) return true;
            }
        }
        return false;
    }

    private bool HasAnyExitingLocals(ImmutableArray<ControlFlowRegion> leavingRegions)
    {
        foreach (var r in leavingRegions)
        {
            if (r.Locals.Length > 0 || _tryFinallyUsingLocal.ContainsKey(r) ||
                r.CaptureIds.Any(id => _captures.TryGetValue(id, out var capture) && capture.Kind == CaptureKind.SpanEnumerator)) return true;
        }
        return false;
    }

    private List<MirStatement> BuildLeavingRegionStatements(ImmutableArray<ControlFlowRegion> leavingRegions, VibraniumSpan span)
    {
        var stmts = new List<MirStatement>();
        if (leavingRegions.IsDefaultOrEmpty)
        {
            return stmts;
        }

        var exitingIds = new List<int>();
        var disposedUsingIds = new HashSet<int>();

        // LeavingRegions is ordered innermost-to-outermost: each `using var` exits its own TryAndFinally region in exact LIFO order!
        foreach (var region in leavingRegions)
        {
            if (region.Kind == ControlFlowRegionKind.TryAndFinally &&
                _tryFinallyUsingLocal.TryGetValue(region, out var usingEntry) &&
                disposedUsingIds.Add(usingEntry.LocalId))
            {
                string? disposeKey = ResolveDisposeMethodKey(usingEntry.LocalType)
                    ?? ResolveDisposeMethodKeyFromMirType(_locals[usingEntry.LocalId].Type);
                if (disposeKey is not null)
                {
                    stmts.Add(new MirStatement.DisposeResource(usingEntry.LocalId, disposeKey, span));
                }
                else
                {
                    if (!_diagnostics.Any(d => d.Id == VibraniumDiagnosticIds.Unsupported))
                    {
                        EmitDiag(
                            VibraniumDiagnosticIds.Unsupported,
                            "Explicit interface implementations are not supported in Vibranium; declare 'public void Dispose()' directly on the linear type.",
                            span);
                    }
                    if (_locals[usingEntry.LocalId].Type is MirType.ClassType { IsDisposable: true } ct)
                    {
                        _locals[usingEntry.LocalId] = _locals[usingEntry.LocalId] with { Type = ct with { IsDisposable = false } };
                    }
                }
            }

            // Enumeration captures are synthetic, not Roslyn source locals. Their
            // lexical loan must end on every real exit edge, including break/return.
            foreach (var captureId in region.CaptureIds)
            {
                if (_captures.TryGetValue(captureId, out var capture) && capture.Kind == CaptureKind.SpanEnumerator)
                {
                    exitingIds.Add(capture.PrimaryLocalId);
                }
            }

            if (!region.Locals.IsEmpty)
            {
                foreach (var locSym in region.Locals)
                {
                    if (_localMap.TryGetValue(locSym, out int lid))
                    {
                        exitingIds.Add(lid);
                    }
                }
            }
        }

        if (exitingIds.Count > 0)
        {
            stmts.Add(new MirStatement.ScopeExit(exitingIds, span));
        }

        return stmts;
    }

    private string? ResolveDisposeMethodKeyFromMirType(MirType mirType)
    {
        if (mirType is MirType.ClassType cls &&
            _module.Classes.TryGetValue(cls.Name, out var cdef) &&
            cdef.DisposeMethodKey is not null)
        {
            return cdef.DisposeMethodKey;
        }
        return null;
    }

    private void EmitLeavingRegionsInline(ImmutableArray<ControlFlowRegion> leavingRegions, VibraniumSpan span)
    {
        var stmts = BuildLeavingRegionStatements(leavingRegions, span);
        if (stmts.Count > 0)
        {
            _currentStmts.AddRange(stmts);
        }
    }

    private int ResolveChainedBranchTargetWithLeavingRegions(
        ControlFlowBranch firstBranch,
        ControlFlowBranch secondBranch,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        int targetId = ResolveBranchTargetWithLeavingRegions(secondBranch, span, orderedBlockIds);
        var firstStmts = BuildLeavingRegionStatements(firstBranch.LeavingRegions, span);
        if (firstStmts.Count == 0)
        {
            return targetId;
        }

        int trampId = _nextSyntheticBlockId++;
        orderedBlockIds.Add(trampId);
        _mirBlockStmts[trampId] = firstStmts;
        _mirBlockTerms[trampId] = new MirTerminator.Goto(targetId, span);
        return trampId;
    }

    private int ResolveBranchTargetWithLeavingRegions(
        ControlFlowBranch branch,
        VibraniumSpan span,
        List<int> orderedBlockIds)
    {
        if (branch.Destination is null)
        {
            int unreachId = _nextSyntheticBlockId++;
            orderedBlockIds.Add(unreachId);
            _mirBlockStmts[unreachId] = new List<MirStatement>();
            _mirBlockTerms[unreachId] = new MirTerminator.Unreachable(span);
            return unreachId;
        }

        int targetOrd = branch.Destination.Ordinal;
        var stmts = BuildLeavingRegionStatements(branch.LeavingRegions, span);
        if (stmts.Count == 0)
        {
            return targetOrd;
        }

        int trampId = _nextSyntheticBlockId++;
        orderedBlockIds.Add(trampId);
        _mirBlockStmts[trampId] = stmts;
        _mirBlockTerms[trampId] = new MirTerminator.Goto(targetOrd, span);
        return trampId;
    }

    private string? ResolveDisposeMethodKey(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol named)
        {
            var disposeMethod = named.GetMembers("Dispose")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m =>
                    !m.IsStatic &&
                    m.MethodKind == MethodKind.Ordinary &&
                    m.DeclaredAccessibility == Accessibility.Public &&
                    m.Parameters.Length == 0 &&
                    m.TypeParameters.Length == 0 &&
                    m.ReturnsVoid);
            if (disposeMethod is not null)
            {
                _enqueueMethodInstantiation(disposeMethod);
                return VibraniumSymbolHelpers.GetMethodKey(disposeMethod);
            }
            string typeKey = VibraniumSymbolHelpers.GetTypeKey(named);
            if (_module.Classes.TryGetValue(typeKey, out var cdef) && cdef.DisposeMethodKey is not null)
            {
                return cdef.DisposeMethodKey;
            }
        }
        return null;
    }
}
