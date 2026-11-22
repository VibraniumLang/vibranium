using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Vibranium.Frontend.Roslyn;

internal sealed partial class SubsetDeclarationCuts
{
    internal const string RefReadonlyParameterMessage = "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter.";
    internal const string UnderscoreDeclarationMessage = "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard.";
    internal const string OptionalParameterMessage = "Optional parameters are not supported in Vibranium v0.2; declare an overload instead.";
    internal const string NamedArgumentOrderMessage = "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order.";
    internal const string ThisAssignmentMessage = "Assigning to this is not supported in Vibranium v0.2; assign the fields individually.";
    internal const string ConditionalRefMessage = "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement.";
    internal const string NestedCollectionMessage = "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first.";
    internal const string NativeIntegerMessage = "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong.";
    internal const string DirectSwitchFunctionMessage = "Local functions must be declared inside a block; declaring one directly in a switch section is not supported in Vibranium v0.2.";
    internal const string SwitchDeclarationMessage = "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }.";
    internal const string ConstantConditionMessage = "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition.";
    internal const string RelationalPatternMessage = "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition.";

    private readonly HashSet<string> _nativeIntegerAliases = new();

    private void CheckExpressionCuts(SyntaxNode root, SemanticModel model)
    {
        // Retain the earliest *anchor*, not the first visited node: an outer and
        // pattern is visited before a relational pattern to its left, for example.
        var first = new Dictionary<string, SyntaxToken>();
        void Report(SyntaxToken token, string message, bool rejectDeclaration = true)
        {
            if (!first.TryGetValue(message, out var earlier) || token.SpanStart < earlier.SpanStart)
                first[message] = token;
            // A cut body must never reach lowering. Reject only its declaration,
            // leaving other members and other cuts in this file independently visible.
            var declaration = token.Parent?.AncestorsAndSelf().FirstOrDefault(node =>
                node is MemberDeclarationSyntax and not NamespaceDeclarationSyntax and not FileScopedNamespaceDeclarationSyntax);
            if (rejectDeclaration && declaration is not null) _rejected.Add(declaration);
        }

        foreach (var node in root.DescendantNodesAndSelf())
        {
            // C1 remains independently visible even when an earlier C18
            // declaration (or another cut) rejects this whole method body.
            if (node is LocalFunctionStatementSyntax { Parent: SwitchSectionSyntax } directFunction)
                Add(directFunction.Identifier, DirectSwitchFunctionMessage);
            if (node is ExpressionSyntax expression)
            {
                var typeInfo = model.GetTypeInfo(expression);
                if (HasNonCopySpanElement(typeInfo.Type) || HasNonCopySpanElement(typeInfo.ConvertedType))
                    Report(expression.GetFirstToken(), NonCopySpanElementMessage);
            }
            if (node is ParameterSyntax parameter)
            {
                if (parameter.Modifiers.Any(SyntaxKind.RefKeyword) && parameter.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                    Report(parameter.Modifiers.First(token => token.IsKind(SyntaxKind.RefKeyword)), RefReadonlyParameterMessage);
                if (parameter.Default is not null || model.GetDeclaredSymbol(parameter) is IParameterSymbol parameterSymbol &&
                    parameterSymbol.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                        "System.Runtime.InteropServices.OptionalAttribute" or "System.Runtime.InteropServices.DefaultParameterValueAttribute"))
                    Report(parameter.GetFirstToken(), OptionalParameterMessage);
            }

            var identifier = DeclarationIdentifier(node);
            if (identifier.ValueText == "_" && model.GetDeclaredSymbol(node) is { Name: "_" } declared &&
                declared.Kind != SymbolKind.Discard && declared is not IParameterSymbol { IsDiscard: true })
                Report(identifier, UnderscoreDeclarationMessage);
            if (node is IdentifierNameSyntax namespaceName && namespaceName.Identifier.ValueText == "_" &&
                namespaceName.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault() is { } namespaceDeclaration &&
                namespaceDeclaration.Name.Span.Contains(namespaceName.Span) && model.GetSymbolInfo(namespaceName).Symbol is INamespaceSymbol { Name: "_" })
                Report(namespaceName.Identifier, UnderscoreDeclarationMessage);

            if (node is ArgumentSyntax { NameColon: { } name } argument &&
                argument.Parent is BaseArgumentListSyntax argumentList &&
                ArgumentParameterOrdinal(argument, model) is int ordinal && ordinal != argumentList.Arguments.IndexOf(argument))
                Report(name.Name.Identifier, NamedArgumentOrderMessage);
            if (node is AttributeArgumentSyntax { NameColon: { } attributeName } attributeArgument &&
                attributeArgument.Parent is AttributeArgumentListSyntax attributeList &&
                model.GetSymbolInfo(attributeList.Parent!).Symbol is IMethodSymbol constructor &&
                constructor.Parameters.FirstOrDefault(p => p.Name == attributeName.Name.Identifier.ValueText) is { } attributeParameter &&
                attributeParameter.Ordinal != attributeList.Arguments.IndexOf(attributeArgument))
                Report(attributeName.Name.Identifier, NamedArgumentOrderMessage);

            if (node is AssignmentExpressionSyntax assignment)
                foreach (var target in ThisAssignmentTargets(assignment.Left)) Report(target.Token, ThisAssignmentMessage);
            if (node is PrefixUnaryExpressionSyntax prefix && prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression &&
                Unwrap(prefix.Operand) is ThisExpressionSyntax prefixThis) Report(prefixThis.Token, ThisAssignmentMessage);
            if (node is PostfixUnaryExpressionSyntax postfix && postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression &&
                Unwrap(postfix.Operand) is ThisExpressionSyntax postfixThis) Report(postfixThis.Token, ThisAssignmentMessage);
            if (node is RefExpressionSyntax reference && Unwrap(reference.Expression) is ThisExpressionSyntax boundThis && IsWritableReference(reference, model))
                Report(boundThis.Token, ThisAssignmentMessage);
            if (node is ArgumentSyntax writableArgument && writableArgument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword &&
                Unwrap(writableArgument.Expression) is ThisExpressionSyntax passedThis)
                Report(passedThis.Token, ThisAssignmentMessage);

            if (node is ConditionalExpressionSyntax conditional &&
                (conditional.WhenTrue is RefExpressionSyntax || conditional.WhenFalse is RefExpressionSyntax))
                Report(conditional.QuestionToken, ConditionalRefMessage);
            if (node is CollectionExpressionSyntax collection && IsNestedCollection(collection))
                Report(collection.OpenBracketToken, NestedCollectionMessage);

            if (node is TypeSyntax or MemberAccessExpressionSyntax && IsNativeIntegerTypeName(node, model))
                Report(node.GetFirstToken(), NativeIntegerMessage);

            if (node is StatementSyntax statement && statement.Parent is SwitchSectionSyntax section &&
                statement is not BlockSyntax && DeclaresSwitchLocal(statement, section, model))
                Report(statement.GetFirstToken(), SwitchDeclarationMessage, rejectDeclaration: statement is not LocalFunctionStatementSyntax);

            ExpressionSyntax? condition = node switch
            {
                IfStatementSyntax value => value.Condition,
                WhileStatementSyntax value => value.Condition,
                DoStatementSyntax value => value.Condition,
                ForStatementSyntax value => value.Condition,
                ConditionalExpressionSyntax value => value.Condition,
                WhenClauseSyntax value => value.Condition,
                SwitchStatementSyntax value => value.Expression,
                SwitchExpressionSyntax value => value.GoverningExpression,
                _ => null,
            };
            if (condition is not null && model.GetConstantValue(condition).HasValue &&
                !(node is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax && condition.IsKind(SyntaxKind.TrueLiteralExpression)))
                Report(condition.GetFirstToken(), ConstantConditionMessage);

            if (node is RelationalPatternSyntax relational) Report(relational.OperatorToken, RelationalPatternMessage);
            if (node is BinaryPatternSyntax binary && binary.IsKind(SyntaxKind.AndPattern)) Report(binary.OperatorToken, RelationalPatternMessage);
        }
        foreach (var cut in first.OrderBy(pair => pair.Value.SpanStart)) Add(cut.Value, cut.Key);
    }

    private static int? ArgumentParameterOrdinal(ArgumentSyntax argument, SemanticModel model)
    {
        // GetSymbolInfo exposes the reduced signature for an extension call;
        // IArgumentOperation can instead refer to its unreduced (receiver-first)
        // parameter. The written argument list must be compared to the former.
        string name = argument.NameColon!.Name.Identifier.ValueText;
        var target = model.GetSymbolInfo(argument.Parent!.Parent!).Symbol;
        return target switch
        {
            IMethodSymbol method => method.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Ordinal,
            IPropertySymbol property => property.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Ordinal,
            _ => (model.GetOperation(argument) as IArgumentOperation)?.Parameter?.Ordinal,
        };
    }

    private static SyntaxToken DeclarationIdentifier(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax value => value.Identifier,
        ParameterSyntax value => value.Identifier,
        TypeParameterSyntax value => value.Identifier,
        BaseTypeDeclarationSyntax value => value.Identifier,
        DelegateDeclarationSyntax value => value.Identifier,
        MethodDeclarationSyntax value => value.Identifier,
        LocalFunctionStatementSyntax value => value.Identifier,
        PropertyDeclarationSyntax value => value.Identifier,
        EventDeclarationSyntax value => value.Identifier,
        EnumMemberDeclarationSyntax value => value.Identifier,
        LabeledStatementSyntax value => value.Identifier,
        ForEachStatementSyntax value => value.Identifier,
        CatchDeclarationSyntax value => value.Identifier,
        SingleVariableDesignationSyntax value => value.Identifier,
        UsingDirectiveSyntax { Alias: { } alias } => alias.Name.Identifier,
        ExternAliasDirectiveSyntax value => value.Identifier,
        FromClauseSyntax value => value.Identifier,
        LetClauseSyntax value => value.Identifier,
        JoinClauseSyntax value => value.Identifier,
        JoinIntoClauseSyntax value => value.Identifier,
        QueryContinuationSyntax value => value.Identifier,
        TupleElementSyntax value => value.Identifier,
        ArgumentSyntax { NameColon: { } name } => name.Name.Identifier,
        AnonymousObjectMemberDeclaratorSyntax { NameEquals: { } name } => name.Name.Identifier,
        _ => default,
    };

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses) expression = parentheses.Expression;
        return expression;
    }

    private static IEnumerable<ThisExpressionSyntax> ThisAssignmentTargets(ExpressionSyntax expression)
    {
        expression = Unwrap(expression);
        if (expression is ThisExpressionSyntax target) yield return target;
        if (expression is TupleExpressionSyntax tuple)
            foreach (var argument in tuple.Arguments)
                foreach (var nestedTarget in ThisAssignmentTargets(argument.Expression)) yield return nestedTarget;
    }

    private static bool IsWritableReference(RefExpressionSyntax reference, SemanticModel model)
    {
        SyntaxNode expression = reference;
        while (expression.Parent is ParenthesizedExpressionSyntax or ConditionalExpressionSyntax) expression = expression.Parent;
        if (expression.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } &&
            model.GetDeclaredSymbol(variable) is ILocalSymbol { RefKind: RefKind.RefReadOnly }) return false;
        if (expression.Parent is AssignmentExpressionSyntax assignment &&
            model.GetSymbolInfo(assignment.Left).Symbol is ILocalSymbol { RefKind: RefKind.RefReadOnly }) return false;
        if (expression.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax &&
            model.GetEnclosingSymbol(reference.SpanStart) is IMethodSymbol { ReturnsByRefReadonly: true }) return false;
        return true;
    }

    private static bool IsNestedCollection(CollectionExpressionSyntax collection)
    {
        SyntaxNode expression = collection;
        while (expression.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax or CheckedExpressionSyntax)
            expression = expression.Parent;
        return expression.Parent is CollectionElementSyntax or SwitchExpressionArmSyntax ||
            expression.Parent is ConditionalExpressionSyntax conditional && expression != conditional.Condition;
    }

    private bool IsNativeIntegerTypeName(SyntaxNode type, SemanticModel model)
    {
        string? name = type switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };
        if (name is null || name is not ("nint" or "nuint" or "IntPtr" or "UIntPtr") && !_nativeIntegerAliases.Contains(name)) return false;
        // IdentifierNameSyntax is also the syntax of a value expression. Binding to
        // a type symbol (rather than merely having that type) avoids flagging uses
        // of IntPtr-valued locals, or a user-defined type called nint/IntPtr.
        var symbol = model.GetSymbolInfo(type).Symbol;
        if (symbol is IAliasSymbol alias) symbol = alias.Target;
        return symbol is INamedTypeSymbol { SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr };
    }

    private const string NonCopySpanElementMessage = "Span<T> and ReadOnlySpan<T> element types must be Copy in Vibranium v0.2.";

    private static bool HasNonCopySpanElement(ITypeSymbol? type) =>
        VibraniumSymbolHelpers.IsSpanType(type, out var element, out _) &&
        !VibraniumSymbolHelpers.IsCopyTypeSymbol(element);

    private static bool DeclaresSwitchLocal(StatementSyntax statement, SwitchSectionSyntax section, SemanticModel model)
    {
        if (statement is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax) return true;
        if (section.Parent is not SwitchStatementSyntax) return false;
        foreach (var designation in statement.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
        {
            if (model.GetDeclaredSymbol(designation) is not ILocalSymbol local) continue;
            // Roslyn decides the scope of out/pattern/deconstruction declarations.
            // A nested block, loop or embedded statement can keep the name local;
            // only symbols visible at the case label leak into the switch list.
            // The closing brace belongs to the outer binder and cannot be used.
            if (model.LookupSymbols(section.GetFirstToken().SpanStart, name: local.Name)
                .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, local))) return true;
        }
        return false;
    }
}
