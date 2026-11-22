using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Vibranium.Compiler.Core;

namespace Vibranium.Frontend.Roslyn;

// SPEC 2 (Banned Operations on Linear Unions): a switch statement or expression that consumes a linear
// Option<T> / Result<T, E> rvalue (a call, Move(x)) tests each variant in at most one arm, with no 'when'
// guard, and uses no 'not' or 'or' pattern. Each of these would test again a value that an earlier arm
// already consumed, so each such arm is rejected with VIB006 at the first token of its pattern.
// A place scrutinee is already rejected as an in-place borrow match (SPEC 3.4) and gets nothing more.
internal static class LinearUnionSwitchBan
{
    internal const string Message =
        "A switch over a linear union value may test each variant in at most one arm, without a 'when' guard, and may not use 'not' or 'or' patterns; branch inside the arm instead.";

    private static readonly string[] s_variantTypeNames = ["Some", "NoneType", "Ok", "Err"];

    // Whether a switch over a linear union rvalue breaks the rule, and the anchors of the arms to report, in
    // source order. A variant arm with a nested subpattern already gets its own VIB006 (SPEC 2, nested
    // subpatterns), so it is not reported again here, but it still breaks the rule when guarded or repeated.
    internal static (bool Violates, List<VibraniumSpan> Reported) Classify(SyntaxNode switchSyntax, SemanticModel model, CancellationToken cancellationToken = default)
    {
        var banned = new List<VibraniumSpan>();
        bool violates = false;
        var arms = new List<(IPatternOperation Pattern, bool Guarded, SyntaxNode PatternSyntax)>();
        switch (model.GetOperation(switchSyntax, cancellationToken))
        {
            case ISwitchOperation switchStatement:
                if (!IsLinearUnionRvalue(switchStatement.Value)) return (false, banned);
                foreach (var section in switchStatement.Cases)
                {
                    foreach (var clause in section.Clauses)
                    {
                        if (clause is IPatternCaseClauseOperation patternClause)
                        {
                            var patternSyntax = clause.Syntax switch
                            {
                                CasePatternSwitchLabelSyntax label => label.Pattern,
                                CaseSwitchLabelSyntax label => label.Value,
                                _ => patternClause.Pattern.Syntax,
                            };
                            arms.Add((patternClause.Pattern, patternClause.Guard is not null, patternSyntax));
                        }
                    }
                }
                break;
            case ISwitchExpressionOperation switchExpression:
                if (!IsLinearUnionRvalue(switchExpression.Value)) return (false, banned);
                foreach (var arm in switchExpression.Arms)
                {
                    arms.Add((arm.Pattern, arm.Guard is not null, (arm.Syntax as SwitchExpressionArmSyntax)?.Pattern ?? arm.Pattern.Syntax));
                }
                break;
            default:
                return (false, banned);
        }

        var tested = new HashSet<string>();
        foreach (var (pattern, guarded, patternSyntax) in arms)
        {
            bool reject;
            bool report = true;
            if (pattern is INegatedPatternOperation or IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or })
            {
                reject = true;
            }
            else if (VariantOf(pattern) is { } variant)
            {
                // Every variant arm records its variant, guarded or not.
                bool repeated = !tested.Add(variant);
                reject = guarded || repeated;
                report = !HasNestedSubpattern(pattern);
            }
            else
            {
                reject = false; // '_', 'default', 'var u' and anything else keep their own rules.
            }

            if (reject)
            {
                violates = true;
                if (report)
                {
                    banned.Add(VibraniumSymbolHelpers.ToVibraniumSpan(patternSyntax.GetFirstToken().GetLocation()));
                }
            }
        }

        return (violates, banned);
    }

    // 'Some<R> { ... }', or 'Some<R>(p)' whose payload pattern p is not 'var v', 'R v' or '_'.
    private static bool HasNestedSubpattern(IPatternOperation pattern) =>
        pattern is IRecursivePatternOperation recursive &&
        (!recursive.PropertySubpatterns.IsEmpty ||
         recursive.DeconstructionSubpatterns.Any(static sub => sub is not (IDeclarationPatternOperation or IDiscardPatternOperation)));

    // A linear Option<T> / Result<T, E> scrutinee that the switch consumes: anything but a local,
    // parameter, field or 'this' place.
    private static bool IsLinearUnionRvalue(IOperation scrutinee)
    {
        var type = scrutinee.Type;
        if (!(VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Option") || VibraniumSymbolHelpers.IsVibraniumCoreType(type, "Result")) ||
            !VibraniumSymbolHelpers.IsLinearResourceSymbol(type))
        {
            return false;
        }

        while (scrutinee is IConversionOperation { IsImplicit: true } conversion)
        {
            scrutinee = conversion.Operand;
        }

        return scrutinee is not (ILocalReferenceOperation or IParameterReferenceOperation or IFieldReferenceOperation or IInstanceReferenceOperation);
    }

    // Some/NoneType/Ok/Err for a pattern that tests one variant, whether it binds the payload, names the
    // wrapper or only tests the variant ('Some<R>(var v)', 'Some<R> s', 'NoneType'); null otherwise.
    private static string? VariantOf(IPatternOperation pattern)
    {
        ITypeSymbol? matched = pattern switch
        {
            IDeclarationPatternOperation { MatchesNull: false } declaration => declaration.MatchedType,
            ITypePatternOperation type => type.MatchedType,
            IRecursivePatternOperation recursive => recursive.MatchedType,
            _ => null,
        };
        foreach (string name in s_variantTypeNames)
        {
            if (VibraniumSymbolHelpers.IsVibraniumCoreType(matched, name))
            {
                return name;
            }
        }

        return null;
    }
}
