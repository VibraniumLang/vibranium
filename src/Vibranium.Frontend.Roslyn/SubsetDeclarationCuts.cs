using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Vibranium.Compiler.Core;

namespace Vibranium.Frontend.Roslyn;

// Find syntactic boundaries before ordinary checking. A cut rejects its declaration,
// not unrelated declarations in the file; conditional compilation rejects the file.
internal sealed partial class SubsetDeclarationCuts
{
    internal const string ConditionalMessage = "Conditional compilation directives (#if, #elif, #else, #endif, #define, #undef) are not supported in Vibranium v0.2; .NET and native builds must compile the same code.";
    internal const string FileMessage = "File-local types (file class, file struct) are not supported in Vibranium v0.2; declare the type without the file modifier.";
    internal const string PartialMessage = "Partial types and members are not supported in Vibranium v0.2.";
    internal const string NestedMessage = "Types nested inside a generic type are not supported in Vibranium v0.2; declare the type at namespace level.";
    internal const string ChainingMessage = "Constructor chaining (: this(...)) is not supported in Vibranium v0.2; initialize every field in each constructor or use a static factory method.";
    internal const string InitAccessorMessage = "'init' accessors are not supported in Vibranium v0.2; use an explicit backing field and accessor bodies.";
    internal const string BaseConstructorMessage = "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance.";

    internal const string ListPatternMessage = "List patterns are not supported in Vibranium v0.2; compare the length and index the elements.";

    private readonly List<SyntaxNode> _rejected = new();
    private readonly HashSet<VibraniumSpan> _rejectedListPatternTests = new();
    internal List<VibraniumDiagnostic> Diagnostics { get; } = new();

    internal SubsetDeclarationCuts(CSharpCompilation compilation, ISet<SyntaxTree>? trustedManaged, ISet<SyntaxTree>? trustedCatalog)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var aliases = tree.GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>().Where(value => value.Alias is not null);
            foreach (var alias in aliases)
                if (compilation.GetSemanticModel(tree).GetDeclaredSymbol(alias)?.Target is INamedTypeSymbol
                    { SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr })
                    _nativeIntegerAliases.Add(alias.Alias!.Name.Identifier.ValueText);
        }
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (VibraniumSymbolHelpers.IsTrustedManagedAdapterTree(tree, trustedManaged)) continue;
            var root = tree.GetRoot();
            var conditional = root.DescendantTrivia(descendIntoTrivia: true)
                .Select(trivia => trivia.GetStructure()).OfType<DirectiveTriviaSyntax>()
                .FirstOrDefault(directive => directive.Kind() is SyntaxKind.IfDirectiveTrivia or
                    SyntaxKind.ElifDirectiveTrivia or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia or
                    SyntaxKind.DefineDirectiveTrivia or SyntaxKind.UndefDirectiveTrivia);
            if (conditional is not null)
            {
                // Tokens of structured trivia include '#', then its directive keyword.
                Add(conditional.ChildTokens().Skip(1).First(), ConditionalMessage);
                _rejected.Add(root);
            }

            var model = compilation.GetSemanticModel(tree);
            CheckExpressionCuts(root, model);

            var seen = new HashSet<string>();
            var rejectedPatterns = new HashSet<PatternSyntax>();
            foreach (var token in root.DescendantTokens())
            {
                string? message = token.Kind() switch
                {
                    SyntaxKind.FileKeyword => FileMessage,
                    SyntaxKind.PartialKeyword when trustedCatalog?.Contains(tree) != true => PartialMessage,
                    _ => null,
                };
                if (message is null || token.Parent?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault() is not { } declaration) continue;
                _rejected.Add(declaration);
                if (seen.Add(message)) Add(token, message);
            }
            foreach (var node in root.DescendantNodes())
            {
                if (node is ListPatternSyntax or SlicePatternSyntax)
                {
                    var pattern = (PatternSyntax)node;
                    // CFG lowering can report at the enclosing pattern (including
                    // recursive patterns) or the full `is` test, switch label or arm.
                    // Only suppress the latter exact span so the governing value,
                    // guards, arm bodies, and unrelated statements stay checked.
                    var outerPattern = pattern.AncestorsAndSelf().OfType<PatternSyntax>().Last();
                    if (rejectedPatterns.Add(outerPattern))
                    {
                        _rejected.Add(outerPattern);
                        if (outerPattern.Parent is IsPatternExpressionSyntax or CasePatternSwitchLabelSyntax or SwitchExpressionArmSyntax)
                            _rejectedListPatternTests.Add(VibraniumSymbolHelpers.ToVibraniumSpan(outerPattern.Parent));
                    }
                    if (seen.Add(ListPatternMessage)) Add(pattern.GetFirstToken(), ListPatternMessage);
                }
                SyntaxToken identifier = node switch
                {
                    BaseTypeDeclarationSyntax type => type.Identifier,
                    DelegateDeclarationSyntax type => type.Identifier,
                    _ => default,
                };
                if (identifier.RawKind != 0 && node.Ancestors().OfType<TypeDeclarationSyntax>().Any(type => type.TypeParameterList is not null))
                {
                    _rejected.Add(node);
                    if (seen.Add(NestedMessage)) Add(identifier, NestedMessage);
                }
                if (node is AccessorDeclarationSyntax accessor && accessor.IsKind(SyntaxKind.InitAccessorDeclaration) &&
                    accessor.Parent?.Parent is BasePropertyDeclarationSyntax member)
                {
                    // Do not lower a member containing a banned accessor: treating an init as
                    // an ordinary setter can create a shared-receiver mutation diagnostic.
                    _rejected.Add(member);
                    if (seen.Add(InitAccessorMessage)) Add(accessor.Keyword, InitAccessorMessage);
                }
                if (node is AssignmentExpressionSyntax assignment && RejectedInitReceiver(assignment, model) is { } receiver)
                {
                    // A constructor write to an already-rejected init member is not
                    // an independent use of an uninitialized `this`. Leave the
                    // value being assigned and unrelated constructor code checked.
                    _rejected.Add(receiver);
                }
                if (node is ConstructorInitializerSyntax initializer &&
                    initializer.Kind() is SyntaxKind.ThisConstructorInitializer or SyntaxKind.BaseConstructorInitializer)
                {
                    string message = initializer.IsKind(SyntaxKind.ThisConstructorInitializer) ? ChainingMessage : BaseConstructorMessage;
                    _rejected.Add(initializer.Parent!);
                    if (seen.Add(message)) Add(initializer.ThisOrBaseKeyword, message);
                }
            }
        }
    }

    private static ExpressionSyntax? RejectedInitReceiver(AssignmentExpressionSyntax assignment, SemanticModel model)
    {
        ExpressionSyntax? receiver = assignment.Left switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax self } => self,
            ElementAccessExpressionSyntax { Expression: ThisExpressionSyntax self } => self,
            _ => null,
        };
        if (receiver is null || !assignment.Ancestors().Any(node => node is ConstructorDeclarationSyntax) ||
            model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol { SetMethod.IsInitOnly: true } property ||
            model.GetEnclosingSymbol(assignment.SpanStart) is not IMethodSymbol { MethodKind: MethodKind.Constructor } ctor ||
            !SymbolEqualityComparer.Default.Equals(property.ContainingType, ctor.ContainingType) ||
            !property.SetMethod.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax().IsKind(SyntaxKind.InitAccessorDeclaration)))
        {
            return null;
        }
        return receiver;
    }

    internal static bool IsCanonicalCutMessage(string message) => message is
        ConditionalMessage or FileMessage or PartialMessage or NestedMessage or ChainingMessage or BaseConstructorMessage or InitAccessorMessage or
        RefReadonlyParameterMessage or UnderscoreDeclarationMessage or OptionalParameterMessage or NamedArgumentOrderMessage or
        ThisAssignmentMessage or ConditionalRefMessage or NestedCollectionMessage or NativeIntegerMessage or
        SwitchDeclarationMessage or ConstantConditionMessage or RelationalPatternMessage or ListPatternMessage;

    private void Add(SyntaxToken token, string message) => Diagnostics.Add(new VibraniumDiagnostic(
        VibraniumDiagnosticIds.Unsupported, message, VibraniumSymbolHelpers.ToVibraniumSpan(token.GetLocation())));

    internal bool Rejects(SyntaxNode node) => _rejected.Any(rejected =>
        rejected.SyntaxTree == node.SyntaxTree && rejected.FullSpan.Contains(node.Span));

    internal bool Rejects(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Any(reference => Rejects(reference.GetSyntax())) ||
        symbol.Locations.Any(location => location.IsInSource && _rejected.Any(rejected =>
            rejected.SyntaxTree == location.SourceTree && rejected.FullSpan.Contains(location.SourceSpan)));

    internal bool Rejects(VibraniumSpan span) => _rejectedListPatternTests.Contains(span) || _rejected.Any(rejected =>
        rejected.SyntaxTree.FilePath == span.FilePath && rejected.FullSpan.Start <= span.Start &&
        span.Start + span.Length <= rejected.FullSpan.End);
}
