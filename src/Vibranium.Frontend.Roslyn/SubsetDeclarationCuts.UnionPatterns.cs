using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Vibranium.Compiler.Core;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class SubsetDeclarationCuts
{
    private const string UnionPropertyMessage = "Property patterns on Option and Result cases are not supported in Vibranium v0.2; use the positional form, such as Some<T>(var x).";
    private const string NamedUnionPositionMessage = "Named positional elements in union-case patterns are not supported in Vibranium v0.2; drop the element name.";

    // These cuts reject a pattern's success edge, not its containing method or
    // the syntax checked by other gates (e.g. a null constant under a name).
    private readonly HashSet<(SyntaxTree Tree, TextSpan Span)> _rejectedUnionPatterns = new();

    internal void CheckUnionPatternCuts(CSharpCompilation compilation, ISet<SyntaxTree>? trustedManaged)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManaged)) continue;
            SemanticModel? model = null;
            bool reportedPropertyClause = false;
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                // Visit clauses in anchor order: a nested brace in an outer
                // pattern's positional part comes before its property clause.
                if (node is PropertyPatternClauseSyntax { Parent: RecursivePatternSyntax owner } properties)
                {
                    model ??= compilation.GetSemanticModel(tree);
                    if (!CfgMirLowerer.IsUnionArmType(GetRecursivePatternType(owner, model))) continue;
                    _rejectedUnionPatterns.Add((tree, owner.Span));
                    if (!reportedPropertyClause)
                    {
                        Add(properties.OpenBraceToken, UnionPropertyMessage);
                        reportedPropertyClause = true;
                    }
                }
                else if (node is PositionalPatternClauseSyntax { Parent: RecursivePatternSyntax positionalOwner } positional)
                {
                    var namedElement = positional.Subpatterns.FirstOrDefault(
                        subpattern => subpattern.ChildNodes().Any(child => child is NameColonSyntax));
                    if (namedElement is null) continue;
                    model ??= compilation.GetSemanticModel(tree);
                    if (!CfgMirLowerer.IsUnionArmType(GetRecursivePatternType(positionalOwner, model))) continue;
                    _rejectedUnionPatterns.Add((tree, positionalOwner.Span));
                    Diagnostics.Add(new VibraniumDiagnostic(VibraniumDiagnosticIds.Unsupported, NamedUnionPositionMessage,
                        VibraniumSymbolHelpers.ToVibraniumSpan(namedElement)));
                }
            }
        }
    }

    internal bool RejectsUnionPattern(IPatternOperation pattern) => _rejectedUnionPatterns.Count != 0 &&
        pattern.DescendantsAndSelf().OfType<IRecursivePatternOperation>().Any(recursive =>
            _rejectedUnionPatterns.Contains((recursive.Syntax.SyntaxTree, recursive.Syntax.Span)));

    private static ITypeSymbol? GetRecursivePatternType(RecursivePatternSyntax pattern, SemanticModel model) =>
        pattern.Type is { } explicitType
            ? model.GetTypeInfo(explicitType).Type
            : (model.GetOperation(pattern) as IRecursivePatternOperation)?.MatchedType;
}
