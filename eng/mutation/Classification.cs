using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Mutation;

internal sealed record SeverityEntry(string Id, string Severity, string Component, string Reason);

/// <summary>
/// The contract is that a C#-invalid program must be rejected, by self as well as by Stage0 and the Analyzer, whatever its
/// CS IDs are. C#-invalid means at least one C# error under the project's settings: a genuine error (default severity
/// Error) or a warning that Vibranium.App.props promotes explicitly (docs/SPEC.md, C# validity). Accepting one is a defect.
/// This table only sets priority. severity-safety-ids.txt lists safety-relevant IDs, whose acceptance could break memory
/// or type safety, ownership/linearity or a Std/Core invariant: such an accept is P1, or P0 when asan mode shows a memory
/// error. severity-other-ids.txt lists the rest: such an accept is P2 and a known issue. Each file has one
/// <c>ID&lt;TAB&gt;component&lt;TAB&gt;reason</c> line per ID. An ID in neither file is unclassified and is treated as
/// safety-relevant.
/// </summary>
internal sealed class SeverityTable
{
    public const string Safety = "safety";
    public const string Other = "other";
    public const string Unclassified = "unclassified";
    private readonly Dictionary<string, SeverityEntry> _entries;

    private SeverityTable(Dictionary<string, SeverityEntry> entries, string safetyPath, string otherPath)
    {
        _entries = entries;
        SafetyPath = safetyPath;
        OtherPath = otherPath;
    }

    public string SafetyPath { get; }

    public string OtherPath { get; }

    public IReadOnlyDictionary<string, SeverityEntry> Entries => _entries;

    public static SeverityTable Load(string safetyPath, string otherPath)
    {
        var entries = new Dictionary<string, SeverityEntry>(StringComparer.Ordinal);
        void Read(string path, string severity)
        {
            int number = 0;
            foreach (string raw in File.ReadLines(path))
            {
                number++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                string[] parts = line.Split('\t');
                if (parts.Length != 3 || !parts[0].StartsWith("CS", StringComparison.Ordinal) || parts[2].Trim().Length == 0)
                    throw new FormatException($"{path}:{number}: expected ID<TAB>component<TAB>reason.");
                if (!entries.TryAdd(parts[0], new SeverityEntry(parts[0], severity, parts[1].Trim(), parts[2].Trim())))
                    throw new FormatException($"{path}:{number}: {parts[0]} is classified twice.");
            }
        }
        Read(safetyPath, Safety);
        Read(otherPath, Other);
        return new SeverityTable(entries, safetyPath, otherPath);
    }

    public string For(string id) => _entries.TryGetValue(id, out var entry) ? entry.Severity : Unclassified;

    public string Component(string id) => _entries.TryGetValue(id, out var entry) ? entry.Component : "?";

    public string Reason(string id) => _entries.TryGetValue(id, out var entry) ? entry.Reason : "not classified yet: treated as safety-relevant";

    /// <summary>Keys of the program's errors whose severity class is safety-relevant or unclassified.</summary>
    public string[] SafetyIds(IEnumerable<CsError> errors) =>
        errors.Select(Classification.Bucket).Where(id => For(id) != Other).Distinct().Order(StringComparer.Ordinal).ToArray();

    /// <summary>Release priority of a defect accept by severity class: P1 (P0 once asan mode shows a memory error) or P2.</summary>
    public static string Priority(string severity) => severity == Other ? "P2" : "P1";

    /// <summary>
    /// C#-invalid input: a genuine C# error (kind <c>error</c>), or only warnings of which at least one is promoted
    /// explicitly by Vibranium.App.props (kind <c>explicit-warning</c>, the explicit Vibranium warning policy).
    /// </summary>
    public static bool IsInvalid(Finding finding) => finding.ErrorKind != "blanket-warning";

    /// <summary>
    /// A defect: self accepted a C#-invalid program. The first ID only buckets the defect for triage, and the severity
    /// class only sets its priority.
    /// </summary>
    public static bool IsDefect(Finding finding) => finding.Self.Status == "accept" && IsInvalid(finding);

    /// <summary>
    /// Warnings-only input: valid C#. The only CS errors are warnings that only the blanket TreatWarningsAsErrors
    /// promotes; a warning alone does not make a program C#-invalid. Reported separately with self/Stage0 agreement; the
    /// gate does not fail on them.
    /// </summary>
    public static bool IsWarningsOnly(Finding finding) => !IsInvalid(finding);

    /// <summary>
    /// A subset rejection: self rejected a C#-invalid program, but the program also contains a construct that
    /// the v0.2 subset cuts (C10-C20). The rejection may come from the cut, not from the C# error, so it is reported apart
    /// from rejections that show the C# check itself.
    /// </summary>
    public static bool IsSubsetRejection(Finding finding) =>
        IsInvalid(finding) && finding.Self.Status == "reject" && finding.Cuts.Length > 0;
}

internal static class Classification
{
    /// <summary>
    /// The triage key of an error: its CS ID, except that CS0649 (a never-assigned field) is split by Stage0's zero-safety of
    /// the field's type. A zero-safe field is keyed CS0649.zero-safe; anything else, or zero-safety not computed, stays
    /// CS0649. CS0649 is a blanket-promoted warning, so alone either key is warnings-only input. The split only orders triage.
    /// </summary>
    public static string Bucket(CsError error) => error.Id == "CS0649" && error.ZeroSafe == true ? "CS0649.zero-safe" : error.Id;

    public static string ErrorKind(IReadOnlyCollection<CsError> errors) =>
        errors.Any(e => e.Promotion == "error") ? "error"
        : errors.Any(e => e.Promotion == "explicit") ? "explicit-warning" : "blanket-warning";

    public static string[] Cuts(SdkResult sdk)
    {
        var tree = sdk.Compilation.SyntaxTrees.Last();
        var root = tree.GetRoot();
        var model = sdk.Compilation.GetSemanticModel(tree);
        var cuts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            if (node is ParameterSyntax parameter)
            {
                if (parameter.Modifiers.Any(SyntaxKind.RefKeyword) && parameter.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)) cuts.Add("C10");
                if (parameter.Default is not null || parameter.AttributeLists.SelectMany(list => list.Attributes).Any(attribute =>
                    (model.GetSymbolInfo(attribute).Symbol as IMethodSymbol)?.ContainingType.ToDisplayString() is
                        "System.Runtime.InteropServices.OptionalAttribute" or "System.Runtime.InteropServices.DefaultParameterValueAttribute")) cuts.Add("C12");
            }
            if (node is AssignmentExpressionSyntax assignment && TargetsThis(assignment.Left)
                || node is RefExpressionSyntax reference && TargetsThis(reference.Expression) && !IsReadOnlyReference(reference)
                || node is ArgumentSyntax arg && TargetsThis(arg.Expression) && (arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword))) cuts.Add("C14");
            if (node is ConditionalExpressionSyntax conditional && (conditional.WhenTrue is RefExpressionSyntax || conditional.WhenFalse is RefExpressionSyntax)) cuts.Add("C15");
            if (node is CollectionExpressionSyntax collection && TransparentParent(collection) is ExpressionElementSyntax or SpreadElementSyntax or ConditionalExpressionSyntax or SwitchExpressionArmSyntax) cuts.Add("C16");
            if (node is IdentifierNameSyntax id)
            {
                var type = model.GetAliasInfo(id)?.Target as INamedTypeSymbol ?? model.GetSymbolInfo(id).Symbol as INamedTypeSymbol;
                if (type?.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr) cuts.Add("C17");
            }
            if (node is RelationalPatternSyntax || node is BinaryPatternSyntax binary && binary.IsKind(SyntaxKind.AndPattern)) cuts.Add("C20");
            if (node is SwitchSectionSyntax section && section.Parent is SwitchStatementSyntax enclosingSwitch)
            {
                if (section.Statements.Any(s => s is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax)) cuts.Add("C18");
                foreach (var designation in section.Statements.SelectMany(s => s.DescendantNodes()).OfType<SingleVariableDesignationSyntax>())
                {
                    var local = model.GetDeclaredSymbol(designation);
                    if (local is not null && model.LookupSymbols(enclosingSwitch.CloseBraceToken.SpanStart, name: local.Name)
                        .Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, local))) cuts.Add("C18");
                }
            }
            if (node is VariableDeclaratorSyntax { Identifier.ValueText: "_" } or ParameterSyntax { Identifier.ValueText: "_" }
                or BaseTypeDeclarationSyntax { Identifier.ValueText: "_" } or MethodDeclarationSyntax { Identifier.ValueText: "_" }
                or PropertyDeclarationSyntax { Identifier.ValueText: "_" } or LabeledStatementSyntax { Identifier.ValueText: "_" }
                or TypeParameterSyntax { Identifier.ValueText: "_" } or ForEachStatementSyntax { Identifier.ValueText: "_" }) cuts.Add("C11");
            ExpressionSyntax? condition = node switch
            {
                IfStatementSyntax s => s.Condition,
                WhileStatementSyntax s => s.Condition,
                DoStatementSyntax s => s.Condition,
                ForStatementSyntax s => s.Condition,
                ConditionalExpressionSyntax s => s.Condition,
                WhenClauseSyntax s => s.Condition,
                SwitchStatementSyntax s => s.Expression,
                SwitchExpressionSyntax s => s.GoverningExpression,
                _ => null,
            };
            if (condition is not null && !(node is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax && condition.IsKind(SyntaxKind.TrueLiteralExpression))
                && model.GetConstantValue(condition).HasValue) cuts.Add("C19");
            if (node is SingleVariableDesignationSyntax or BaseNamespaceDeclarationSyntax or UsingDirectiveSyntax or CatchDeclarationSyntax
                or FromClauseSyntax or LetClauseSyntax or JoinClauseSyntax or JoinIntoClauseSyntax or QueryContinuationSyntax)
            {
                if (model.GetDeclaredSymbol(node)?.Name == "_") cuts.Add("C11");
            }
            if (node is TupleElementSyntax { Identifier.ValueText: "_" } or EventDeclarationSyntax { Identifier.ValueText: "_" }) cuts.Add("C11");
            if (node is ArgumentSyntax { Parent: TupleExpressionSyntax, NameColon.Name.Identifier.ValueText: "_" }) cuts.Add("C11");
            if (node is BaseNamespaceDeclarationSyntax declaration && declaration.Name.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                .Any(name => name.Identifier.ValueText == "_")) cuts.Add("C11");
            if (node is BaseArgumentListSyntax arguments && arguments.Parent is { } call)
            {
                var info = model.GetSymbolInfo(call);
                var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
                var parameters = symbol switch
                {
                    IMethodSymbol method => method.Parameters,
                    IPropertySymbol property => property.Parameters,
                    _ => default,
                };
                if (!parameters.IsDefault)
                    for (int i = 0; i < arguments.Arguments.Count; i++)
                        if (arguments.Arguments[i].NameColon is { } name
                            && parameters.FirstOrDefault(p => p.Name == name.Name.Identifier.ValueText) is { } formalParameter
                            && parameters.IndexOf(formalParameter) != i) cuts.Add("C13");
            }
        }
        return cuts.Order(StringComparer.Ordinal).ToArray();
    }
    private static bool TargetsThis(ExpressionSyntax expression) => expression switch
    {
        ThisExpressionSyntax => true,
        ParenthesizedExpressionSyntax parenthesized => TargetsThis(parenthesized.Expression),
        TupleExpressionSyntax tuple => tuple.Arguments.Any(argument => TargetsThis(argument.Expression)),
        _ => false,
    };

    private static SyntaxNode? TransparentParent(SyntaxNode node)
    {
        while (node.Parent is ParenthesizedExpressionSyntax parenthesized) node = parenthesized;
        return node.Parent;
    }

    private static bool IsReadOnlyReference(RefExpressionSyntax reference)
    {
        var declaration = reference.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault();
        return declaration?.Type is RefTypeSyntax { ReadOnlyKeyword.RawKind: not 0 };
    }

}
