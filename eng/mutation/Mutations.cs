using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Mutation;

/// <summary>
/// Deterministic single-span mutation operators over real Vibranium sources. Every operator records at most
/// <c>perOperator</c> edits per base, chosen by a stable hash of the edit position so that edits spread over
/// the whole file instead of its first lines. Semantic operators (names starting with <c>sem-</c>) need the
/// base's SDK compilation; without it only the syntactic operators run.
/// </summary>
internal static class Mutations
{
    private static readonly string[] Literals = ["1L", "2147483648", "65536", "300", "-1", "1.5", "\"text\"", "true", "null", "'x'", "int.MaxValue + 1"];

    public static IReadOnlyList<Mutation> Create(string source, int perOperator, SdkResult? sdk = null, MemberCatalog? catalog = null)
    {
        var tree = sdk?.Compilation.SyntaxTrees.Last() ?? CSharpSyntaxTree.ParseText(source, Corpus.ParseOptions);
        if (tree.ToString() != source) throw new InvalidOperationException("Mutation base and its compilation tree differ.");
        var root = tree.GetCompilationUnitRoot();
        var model = sdk?.Compilation.GetSemanticModel(tree);
        var byOperator = new Dictionary<string, List<Mutation>>(StringComparer.Ordinal);
        var order = new List<string>();
        void Edit(string name, int start, int length, string replacement)
        {
            if (source.AsSpan(start, length).SequenceEqual(replacement)) return;
            if (!byOperator.TryGetValue(name, out var list))
            {
                byOperator[name] = list = [];
                order.Add(name);
            }
            list.Add(new Mutation(name, start, length, replacement));
        }
        void Replace(string name, SyntaxNode node, string replacement) => Edit(name, node.SpanStart, node.Span.Length, replacement);
        void ReplaceToken(string name, SyntaxToken token, string replacement) => Edit(name, token.SpanStart, token.Span.Length, replacement);
        void AddModifier(string name, SyntaxNode node, string modifier) => Edit(name, node.SpanStart, 0, modifier + " ");
        void Before(string name, StatementSyntax statement, string text)
        {
            if (statement.Parent is BlockSyntax or SwitchSectionSyntax or GlobalStatementSyntax) Edit(name, statement.SpanStart, 0, text + " ");
        }
        foreach (var node in root.DescendantNodes())
        {
            Syntactic(node);
            if (model is not null) Semantic(node, model);
        }
        var result = new List<Mutation>();
        foreach (string name in order)
        {
            result.AddRange(byOperator[name]
                .OrderBy(m => Files.Hash(name + "@" + m.Position.ToString(CultureInfo.InvariantCulture) + "#" + m.Replacement), StringComparer.Ordinal)
                .Take(perOperator));
        }
        return result;

        void Syntactic(SyntaxNode node)
        {
            if (node is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                foreach (string value in Literals) Replace("literal:" + value, node, value);
            }
            if (node is LiteralExpressionSyntax text && text.IsKind(SyntaxKind.StringLiteralExpression))
                Replace("string-to-int", node, "123");
            if (node is CastExpressionSyntax cast) Replace("remove-cast", cast, cast.Expression.ToString());
            if (node is VariableDeclaratorSyntax variable)
            {
                if (variable.Initializer is not null && variable.Parent is VariableDeclarationSyntax decl && !decl.Type.IsVar)
                    Replace("delete-initializer", variable.Initializer, "");
                if (variable.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax local })
                {
                    Edit("duplicate-local", local.Span.End, 0, "\n" + local);
                    AddModifier("const-local", local, "const");
                    if (local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                        Edit("using-write", local.Span.End, 0, " " + variable.Identifier.ValueText + " = " + variable.Identifier.ValueText + ";");
                }
                if (variable.Parent is VariableDeclarationSyntax declaration && !declaration.Type.IsVar)
                {
                    foreach (string type in new[] { "char", "byte", "bool", "long", "string" })
                        Replace("variable-type:" + type, declaration.Type, type);
                }
                if (variable.Parent is VariableDeclarationSyntax { Type.IsVar: true } varDeclaration && varDeclaration.Parent is LocalDeclarationStatementSyntax)
                {
                    foreach (string type in new[] { "byte", "char", "int", "bool", "string" })
                        Replace("var-type:" + type, varDeclaration.Type, type);
                }
                if (variable.Parent is VariableDeclarationSyntax { Type: RefTypeSyntax refType } && variable.Initializer?.Value is RefExpressionSyntax refValue)
                {
                    Replace("ref-local-drop-ref", refValue, refValue.Expression.ToString());
                    if (refType.ReadOnlyKeyword.IsKind(SyntaxKind.None)) Edit("ref-local-readonly", refType.RefKeyword.Span.End, 0, " readonly");
                }
            }
            if (node is FieldDeclarationSyntax field)
            {
                foreach (string modifier in new[] { "readonly", "static", "const", "private", "protected" })
                    AddModifier("field-add:" + modifier, field, modifier);
                foreach (var modifier in field.Modifiers)
                {
                    Edit("field-drop:" + modifier.ValueText, modifier.SpanStart, modifier.Span.Length, "");
                    if (modifier.IsKind(SyntaxKind.PrivateKeyword) || modifier.IsKind(SyntaxKind.ProtectedKeyword))
                        Edit("field-public", modifier.SpanStart, modifier.Span.Length, "public");
                }
                if (field.Modifiers.Any(SyntaxKind.PublicKeyword))
                {
                    var token = field.Modifiers.First(t => t.IsKind(SyntaxKind.PublicKeyword));
                    Edit("field-private", token.SpanStart, token.Span.Length, "private");
                    Edit("field-protected", token.SpanStart, token.Span.Length, "protected");
                }
            }
            if (node is MemberDeclarationSyntax member && node is not (GlobalStatementSyntax or BaseNamespaceDeclarationSyntax))
            {
                Edit("duplicate-member", member.Span.End, 0, "\n" + member);
                if (member is MethodDeclarationSyntax or PropertyDeclarationSyntax or ConstructorDeclarationSyntax or IndexerDeclarationSyntax or OperatorDeclarationSyntax)
                {
                    foreach (var modifier in member.Modifiers.Where(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword)))
                        ReplaceToken("member-private", modifier, "private");
                }
            }
            if (node is AccessorDeclarationSyntax accessor && accessor.Modifiers.Count == 0 && accessor.Parent is AccessorListSyntax accessors)
            {
                AddModifier("accessor-private", accessor, "private");
                if (accessors.Accessors.Count > 1 && (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) || accessor.IsKind(SyntaxKind.InitAccessorDeclaration)))
                    Replace("accessor-drop-set", accessor, "");
            }
            if (node is MethodDeclarationSyntax method)
            {
                foreach (string modifier in new[] { "static", "readonly", "private", "sealed", "abstract" })
                    AddModifier("method-add:" + modifier, method, modifier);
                foreach (var modifier in method.Modifiers)
                    Edit("method-drop:" + modifier.ValueText, modifier.SpanStart, modifier.Span.Length, "");
                if (method.Body is not null && !method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    Edit("assign-this", method.Body.OpenBraceToken.Span.End, 0, " this = default; ");
                if (method.ReturnType is PredefinedTypeSyntax predefined)
                {
                    if (predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                    {
                        Replace("return-type:int", predefined, "int");
                        if (method.Body is not null) Edit("return-value-in-void", method.Body.OpenBraceToken.Span.End, 0, " return 1; ");
                    }
                    else
                    {
                        foreach (string type in new[] { "byte", "char", "string", "void" }) Replace("return-type:" + type, predefined, type);
                    }
                }
                if (method.ReturnType is RefTypeSyntax refReturn && refReturn.ReadOnlyKeyword.IsKind(SyntaxKind.None))
                    Edit("ref-return-readonly", refReturn.RefKeyword.Span.End, 0, " readonly");
                if (method.Modifiers.Any(SyntaxKind.StaticKeyword) && method.Body is not null
                    && method.Body.DescendantNodes().OfType<IdentifierNameSyntax>().FirstOrDefault(n => n.Parent is not (MemberAccessExpressionSyntax or QualifiedNameSyntax or NameColonSyntax) && n.Parent is ExpressionSyntax or ArgumentSyntax or ExpressionStatementSyntax) is { } firstName)
                    Edit("this-in-static", firstName.SpanStart, 0, "this.");
            }
            if (node is TypeDeclarationSyntax typeDecl)
            {
                foreach (string modifier in new[] { "readonly", "static", "sealed", "private" })
                    AddModifier("type-add:" + modifier, typeDecl, modifier);
                var named = typeDecl.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var firstField = typeDecl.Members.OfType<FieldDeclarationSyntax>().FirstOrDefault();
                if (named is not null && firstField is not null)
                    Edit("member-name-collision", named.Identifier.SpanStart, named.Identifier.Span.Length, firstField.Declaration.Variables[0].Identifier.ValueText);
                if (typeDecl is StructDeclarationSyntax structDecl)
                {
                    ReplaceToken("type-kind:class", structDecl.Keyword, "class");
                    if (structDecl.TypeParameterList is null)
                        Edit("struct-self-field", structDecl.OpenBraceToken.Span.End, 0, " public " + structDecl.Identifier.ValueText + " SelfLoop; ");
                }
                if (typeDecl is ClassDeclarationSyntax classDecl) ReplaceToken("type-kind:struct", classDecl.Keyword, "struct");
            }
            if (node is ParameterListSyntax parameters && parameters.Parameters.Count > 0)
            {
                var first = parameters.Parameters[0];
                if (first.Default is null) Edit("optional-parameter", first.Span.End, 0, " = default");
                else Replace("drop-optional-default", first.Default, "");
                foreach (string modifier in new[] { "in", "out", "ref", "ref readonly", "scoped" })
                    AddModifier("parameter-add:" + modifier, first, modifier);
                foreach (var parameter in parameters.Parameters)
                {
                    var refKind = parameter.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.InKeyword) || m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.OutKeyword));
                    if (refKind.IsKind(SyntaxKind.None)) continue;
                    foreach (string replacement in new[] { "in", "ref", "out", "" })
                        if (replacement != refKind.ValueText) ReplaceToken("parameter-refkind:" + (replacement.Length == 0 ? "none" : replacement), refKind, replacement);
                    if (refKind.IsKind(SyntaxKind.InKeyword) && parameters.Parent is BaseMethodDeclarationSyntax { Body: { } inBody })
                        Edit("in-parameter-write", inBody.OpenBraceToken.Span.End, 0, " " + parameter.Identifier.ValueText + " = " + parameter.Identifier.ValueText + "; ");
                    if (refKind.IsKind(SyntaxKind.OutKeyword) && parameters.Parent is BaseMethodDeclarationSyntax { Body: { } outBody })
                    {
                        var assignment = outBody.DescendantNodes().OfType<ExpressionStatementSyntax>().FirstOrDefault(s => s.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax target } && target.Identifier.ValueText == parameter.Identifier.ValueText);
                        if (assignment is not null) Replace("delete-out-assignment", assignment, "");
                    }
                }
                if (first.Type is not null)
                    foreach (string type in new[] { "byte", "char", "long", "string" }) Replace("parameter-type:" + type, first.Type, type);
                if (parameters.Parameters.Count > 1)
                {
                    var last = parameters.Parameters[^1];
                    Edit("parameter-name-collision", last.Identifier.SpanStart, last.Identifier.Span.Length, first.Identifier.ValueText);
                    Replace("swap-parameters", parameters, "(" + string.Join(", ", parameters.Parameters.Reverse()) + ")");
                }
            }
            if (node is ArgumentListSyntax arguments)
            {
                if (arguments.Arguments.Count > 0)
                {
                    Replace("drop-argument", arguments, "(" + string.Join(", ", arguments.Arguments.SkipLast(1)) + ")");
                    var first = arguments.Arguments[0];
                    foreach (string modifier in new[] { "in", "out", "ref" }) AddModifier("argument-add:" + modifier, first, modifier);
                    var last = arguments.Arguments[^1];
                    if (last != first && last.RefKindKeyword.IsKind(SyntaxKind.None))
                        foreach (string modifier in new[] { "out", "ref" }) AddModifier("argument-add-last:" + modifier, last, modifier);
                    if (arguments.Arguments.Count > 1)
                        Replace("swap-arguments", arguments, "(" + string.Join(", ", arguments.Arguments.Reverse()) + ")");
                }
                Replace("extra-argument", arguments, "(" + string.Join(", ", arguments.Arguments.Select(a => a.ToString()).Append("123")) + ")");
            }
            if (node is ArgumentSyntax argument)
            {
                if (!argument.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    ReplaceToken("argument-drop-refkind", argument.RefKindKeyword, "");
                    foreach (string replacement in new[] { "in", "ref", "out" })
                        if (replacement != argument.RefKindKeyword.ValueText) ReplaceToken("argument-refkind:" + replacement, argument.RefKindKeyword, replacement);
                }
                if (argument.Expression is IdentifierNameSyntax plain)
                    foreach (string type in new[] { "byte", "string", "bool" }) Replace("cast:" + type, plain, "(" + type + ")" + plain);
            }
            if (node is ReturnStatementSyntax ret)
            {
                Replace("delete-return", ret, "");
                if (ret.Expression is not null) Replace("delete-return-value", ret.Expression, "");
                if (ret.Expression is RefExpressionSyntax refReturned)
                    Replace("ref-return-local", ret, "{ var refEscape = " + refReturned.Expression + "; return ref refEscape; }");
            }
            if (node is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax } statement)
                Replace("delete-assignment", statement, "");
            if (node is AssignmentExpressionSyntax assignmentExpression)
            {
                Replace("assignment-target-this", assignmentExpression.Left, "this");
                Replace("assignment-target-literal", assignmentExpression.Left, "123");
                if (assignmentExpression.IsKind(SyntaxKind.SimpleAssignmentExpression))
                    foreach (string op in new[] { "+=", "-=", "|=" }) ReplaceToken("compound-assign:" + op, assignmentExpression.OperatorToken, op);
            }
            if (node is BinaryExpressionSyntax binary)
            {
                string? swapped = binary.Kind() switch
                {
                    SyntaxKind.AddExpression or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression => "&&",
                    SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression => "+",
                    SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression => "+",
                    SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression => "<",
                    SyntaxKind.BitwiseAndExpression or SyntaxKind.BitwiseOrExpression => "&&",
                    _ => null,
                };
                if (swapped is not null) ReplaceToken("binary-operator:" + swapped, binary.OperatorToken, swapped);
                if (binary.IsKind(SyntaxKind.AddExpression)) ReplaceToken("binary-operator:<<", binary.OperatorToken, "<<");
            }
            if (node is PrefixUnaryExpressionSyntax unary)
            {
                if (unary.IsKind(SyntaxKind.UnaryMinusExpression)) ReplaceToken("unary-operator:!", unary.OperatorToken, "!");
                if (unary.IsKind(SyntaxKind.LogicalNotExpression)) ReplaceToken("unary-operator:-", unary.OperatorToken, "-");
            }
            if (node is ConditionalExpressionSyntax conditional && conditional.WhenFalse is not RefExpressionSyntax)
                Replace("conditional-branch-type", conditional.WhenFalse, "\"text\"");
            if (node is IdentifierNameSyntax name && !(name.Parent is MemberAccessExpressionSyntax nameOwner && nameOwner.Name == name)
                && name.Parent is ExpressionSyntax or ArgumentSyntax or ExpressionStatementSyntax or EqualsValueClauseSyntax or ReturnStatementSyntax)
                ReplaceToken("rename-identifier", name.Identifier, name.Identifier.ValueText + "Zz");
            if (node is MemberAccessExpressionSyntax memberAccess)
                ReplaceToken("rename-member", memberAccess.Name.Identifier, memberAccess.Name.Identifier.ValueText + "Zz");
            if (node is LabeledStatementSyntax label)
                Edit("duplicate-label", label.Span.End, 0, "\n" + label.Identifier.ValueText + ": ;");
            if (node is TypeArgumentListSyntax types)
            {
                Replace("generic-extra-arity", types, "<" + string.Join(", ", types.Arguments.Select(a => a.ToString()).Append("int")) + ">");
                if (types.Arguments.Count > 0)
                {
                    Replace("generic-argument-string", types.Arguments[0], "string");
                    Replace("generic-argument:Vec<int>", types.Arguments[0], "Vec<int>");
                    string? userClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault(c => c.TypeParameterList is null)?.Identifier.ValueText;
                    if (userClass is not null) Replace("generic-argument:class", types.Arguments[0], userClass);
                }
            }
            if (node is TypeParameterConstraintClauseSyntax constraint)
            {
                Replace("constraint-struct", constraint, "where " + constraint.Name + " : struct");
                Replace("constraint-class", constraint, "where " + constraint.Name + " : class");
                Replace("constraint-unmanaged", constraint, "where " + constraint.Name + " : unmanaged");
                Replace("constraint-drop", constraint, "");
            }
            if (node is MethodDeclarationSyntax { TypeParameterList: { } typeParams, Body: { } body, ConstraintClauses.Count: 0 })
                Edit("add-struct-constraint", body.SpanStart, 0, "where " + typeParams.Parameters[0].Identifier.ValueText + " : struct ");
            if (node is ExpressionStatementSyntax expression)
            {
                Before("misplaced-break", expression, "break;");
                Before("misplaced-continue", expression, "continue;");
            }
            if (node is ForEachStatementSyntax { Statement: BlockSyntax foreachBody } forEach)
                Edit("foreach-write", foreachBody.OpenBraceToken.Span.End, 0, " " + forEach.Identifier.ValueText + " = " + forEach.Identifier.ValueText + "; ");
            if (node is SwitchSectionSyntax section)
            {
                if (section.Statements.Count > 0 && section.Statements[^1] is BreakStatementSyntax or ReturnStatementSyntax or ContinueStatementSyntax)
                    Replace("switch-drop-terminator", section.Statements[^1], "");
                if (section.Labels.FirstOrDefault(l => l is CaseSwitchLabelSyntax) is { } caseLabel)
                    Edit("duplicate-case-label", section.Labels[^1].Span.End, 0, " " + caseLabel);
                if (section.Parent is SwitchStatementSyntax owner && owner.Sections.IndexOf(section) is int index && index > 0
                    && owner.Sections[0].Labels.FirstOrDefault(l => l is CaseSwitchLabelSyntax) is { } firstLabel)
                    Edit("duplicate-case-across", section.Labels[^1].Span.End, 0, " " + firstLabel);
            }
            if (node is SwitchExpressionSyntax switchExpression && switchExpression.Arms.Count > 1)
            {
                var arms = switchExpression.Arms;
                Edit("switch-expression-drop-last-arm", arms[^2].Span.End, arms[^1].Span.End - arms[^2].Span.End, "");
                Edit("switch-expression-drop-first-arm", arms[0].SpanStart, arms[1].SpanStart - arms[0].SpanStart, "");
            }
            if (node is BlockSyntax block && block.Statements.Count > 1)
            {
                for (int i = 0; i + 1 < block.Statements.Count; i++)
                {
                    var a = block.Statements[i];
                    var b = block.Statements[i + 1];
                    Edit("swap-adjacent-statements", a.SpanStart, b.Span.End - a.SpanStart, b + "\n" + a);
                }
            }
            if (node is LocalDeclarationStatementSyntax declarationStatement && declarationStatement.Parent is BlockSyntax or GlobalStatementSyntax or SwitchSectionSyntax)
                Replace("delete-declaration", declarationStatement, "");
        }

        void Semantic(SyntaxNode node, SemanticModel semantic)
        {
            if (node is MemberAccessExpressionSyntax access && access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
            {
                var receiverSymbol = semantic.GetSymbolInfo(access.Expression).Symbol;
                bool staticAccess = receiverSymbol is INamedTypeSymbol;
                var receiverType = staticAccess ? (INamedTypeSymbol)receiverSymbol! : semantic.GetTypeInfo(access.Expression).Type as INamedTypeSymbol;
                if (receiverType is not null && catalog is not null)
                {
                    foreach (var hidden in catalog.Hidden(receiverType, staticAccess))
                        ReplaceToken("sem-internal-member:" + hidden.Kind, access.Name.Identifier, hidden.Name);
                    if (access.Expression is IdentifierNameSyntax && access.Ancestors().OfType<StatementSyntax>().FirstOrDefault() is { } holder)
                        foreach (var hidden in catalog.Hidden(receiverType, staticAccess).Where(h => h.Kind == "field"))
                            Before("sem-internal-field-write", holder, access.Expression + "." + hidden.Name + " = default;");
                }
                var memberSymbol = semantic.GetSymbolInfo(access).Symbol;
                if (access.Expression is IdentifierNameSyntax or ThisExpressionSyntax && access.Ancestors().OfType<StatementSyntax>().FirstOrDefault() is { } statement
                    && !access.Ancestors().Any(a => a is AssignmentExpressionSyntax assigned && assigned.Left.Span.Contains(access.Span)))
                {
                    string write = access + " = " + access + ";";
                    if (memberSymbol is IPropertySymbol { SetMethod: null, IsIndexer: false }) Before("sem-write-getonly-property", statement, write);
                    if (memberSymbol is IFieldSymbol { IsReadOnly: true, IsConst: false }) Before("sem-write-readonly-field", statement, write);
                    if (memberSymbol is IFieldSymbol { IsConst: false } && semantic.GetSymbolInfo(access.Expression).Symbol is IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter })
                        Before("sem-write-in-parameter-field", statement, write);
                    if (memberSymbol is IFieldSymbol { IsConst: false, IsReadOnly: false } && semantic.GetTypeInfo(access.Expression).Type is { IsReadOnly: true })
                        Before("sem-write-readonly-struct-field", statement, write);
                }
                if (memberSymbol is IPropertySymbol { Type: INamedTypeSymbol { TypeKind: TypeKind.Struct } propertyType, IsIndexer: false }
                    && access.Ancestors().OfType<StatementSyntax>().FirstOrDefault() is { } propertyStatement
                    && propertyType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => !f.IsStatic && !f.IsConst && f.DeclaredAccessibility == Accessibility.Public) is { } innerField)
                    Before("sem-write-through-property", propertyStatement, access + "." + innerField.Name + " = " + access + "." + innerField.Name + ";");
                if (memberSymbol is IPropertySymbol && access.Parent is ArgumentSyntax { RefKindKeyword.RawKind: 0 } propertyArgument)
                    AddModifier("sem-property-as-ref-argument", propertyArgument, "ref");
                if (memberSymbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } && access.Parent is not MemberAccessExpressionSyntax)
                    Replace("sem-enum-to-int", access, "1");
            }
            if (node is ElementAccessExpressionSyntax element && element.Expression is IdentifierNameSyntax
                && element.Ancestors().OfType<StatementSyntax>().FirstOrDefault() is { } elementStatement
                && semantic.GetTypeInfo(element.Expression).Type is INamedTypeSymbol { Name: "ReadOnlySpan" or "OptionRef" })
                Before("sem-write-readonly-indexer", elementStatement, element + " = " + element + ";");
            if (node is VariableDeclarationSyntax { Type.IsVar: true, Variables.Count: 1 } varDeclaration && varDeclaration.Parent is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: 0 } varStatement
                && varDeclaration.Variables[0].Initializer is not null && semantic.GetTypeInfo(varDeclaration.Type).Type is { } varType && varType.TypeKind != TypeKind.Error)
                Replace("sem-var-uninitialized", varStatement, varType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + " " + varDeclaration.Variables[0].Identifier.ValueText + ";");
            if (node is InvocationExpressionSyntax invocation && catalog is not null && invocation.Parent is ExpressionStatementSyntax callStatement)
            {
                foreach (string call in catalog.HiddenStaticCalls.Take(4))
                    Before("sem-internal-static-call", callStatement, call);
            }
        }
    }
}

internal sealed record HiddenMember(string Name, string Kind);

/// <summary>
/// Non-public members of Vibranium.Core and Vibranium.Std, read from metadata with every member imported,
/// so that mutants can name the internals a user program must never reach.
/// </summary>
internal sealed class MemberCatalog
{
    private readonly Dictionary<string, HiddenMember[]> _instance = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HiddenMember[]> _static = new(StringComparer.Ordinal);

    public MemberCatalog(CSharpCompilation compilation)
    {
        var all = compilation.WithOptions(compilation.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        var calls = new List<string>();
        foreach (var reference in all.References)
        {
            if (all.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly || assembly.Name is not ("Vibranium.Core" or "Vibranium.Std")) continue;
            foreach (var type in Types(assembly.GlobalNamespace))
            {
                string key = Key(type);
                var members = type.GetMembers().Where(m => !m.IsImplicitlyDeclared && m.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
                    && m.CanBeReferencedByName && !m.Name.StartsWith('<')).ToArray();
                HiddenMember[] Select(bool isStatic) => members.Where(m => m.IsStatic == isStatic)
                    .Select(m => new HiddenMember(m.Name, m switch { IFieldSymbol => "field", IPropertySymbol => "property", IMethodSymbol => "method", _ => "other" }))
                    .Where(m => m.Kind != "other").DistinctBy(m => m.Name).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
                _instance[key] = Select(false);
                _static[key] = Select(true);
                bool typeVisible = type.DeclaredAccessibility == Accessibility.Public && type.ContainingType is null;
                foreach (var method in members.OfType<IMethodSymbol>().Where(m => m.IsStatic && m.MethodKind == MethodKind.Ordinary && m.Parameters.Length == 0 && !m.IsGenericMethod && type.TypeParameters.Length == 0))
                    calls.Add("global::" + type.ToDisplayString() + "." + method.Name + "();");
                if (!typeVisible && type.TypeParameters.Length == 0)
                    foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsStatic && m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 0 && !m.IsGenericMethod))
                        calls.Add("global::" + type.ToDisplayString() + "." + method.Name + "();");
            }
        }
        HiddenStaticCalls = calls.Distinct(StringComparer.Ordinal).OrderBy(c => Files.Hash(c), StringComparer.Ordinal).ToArray();
    }

    /// <summary>Zero-argument static calls into internal types or onto non-public static methods of Core/Std.</summary>
    public string[] HiddenStaticCalls { get; }

    public IEnumerable<HiddenMember> Hidden(INamedTypeSymbol type, bool isStatic)
    {
        var table = isStatic ? _static : _instance;
        return table.TryGetValue(Key(type.OriginalDefinition), out var members) ? members : [];
    }

    private static string Key(INamedTypeSymbol type) => type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "`" + type.Arity.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in Nested(type)) yield return nested;
        }
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var type in Types(child)) yield return type;
    }

    private static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in Nested(nested)) yield return deeper;
        }
    }
}
