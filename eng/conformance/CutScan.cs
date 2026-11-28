using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Vibranium.Conformance;

/// <summary>One occurrence of a v0.2 cut: catalog tag, cut id, 1-based anchor position and the exact message.</summary>
internal sealed record CutHit(string Tag, string Cut, int Line, int Col, string Message);

/// <summary>
/// The tool's own, frontend-independent detector of the user-approved cuts C10–C20 and the decided cut C21
/// (docs/CONFORMANCE.md §5.2), run on
/// the Roslyn compilation of every program. Each detected cut adds its <c>cut.*</c> tag, so the banned/cut oracle
/// expects every frontend to reject the program with VIB000 and <see cref="Engine"/> checks the exact message at the
/// anchor of the cut's first occurrence. The generator does not produce these forms; the scan is the safety net
/// (mutations, hand-written probes) and gives gen-stats/campaign counts of cut-form programs.
/// </summary>
internal static class CutScan
{
    public static readonly (string Cut, string Tag, string Message)[] Cuts =
    [
        ("C10", "cut.ref-readonly-param", "ref readonly parameters are not supported in Vibranium v0.2; use an in parameter."),
        ("C11", "cut.underscore-decl", "Declaring an identifier named _ is not supported in Vibranium v0.2; use _ only as a discard."),
        ("C12", "cut.optional-param", "Optional parameters are not supported in Vibranium v0.2; declare an overload instead."),
        ("C13", "cut.named-arg-order", "Named arguments out of parameter order are not supported in Vibranium v0.2; pass arguments in parameter order."),
        ("C14", "cut.this-assign", "Assigning to this is not supported in Vibranium v0.2; assign the fields individually."),
        ("C15", "cut.conditional-ref", "Conditional ref expressions (c ? ref a : ref b) are not supported in Vibranium v0.2; use an if statement."),
        ("C16", "cut.nested-collection", "Collection expressions nested in another collection expression or used as a conditional or switch-expression branch are not supported in Vibranium v0.2; assign the collection to a local first."),
        ("C17", "cut.native-int", "The native-sized integer types nint and nuint are not supported in Vibranium v0.2; use long or ulong."),
        ("C18", "cut.switch-section-decl", "Declarations directly in a switch section are not supported in Vibranium v0.2; wrap the section's statements in a block { }."),
        ("C19", "cut.constant-condition", "Constant conditions, guards and switch selectors are not supported in Vibranium v0.2; only while (true), do ... while (true) and for (;;) may use a constant condition."),
        ("C20", "cut.relational-and-pattern", "Relational patterns and and-patterns are not supported in Vibranium v0.2; use a when guard or an if condition."),
        ("C21", "cut.base-ctor-call", "Base constructor calls (: base(...)) are not supported in Vibranium v0.2; Vibranium has no inheritance."),
    ];

    public static readonly HashSet<string> Tags = [.. Cuts.Select(c => c.Tag)];

    public static string MessageOf(string tag) => Cuts.First(c => c.Tag == tag).Message;

    /// <summary>The first occurrence (source order) of every cut present in <paramref name="tree"/>.</summary>
    public static List<CutHit> Scan(CSharpCompilation comp, SyntaxTree tree)
    {
        var model = comp.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var all = new List<(string Cut, int Pos)>();
        void Hit(string cut, SyntaxToken t) => all.Add((cut, t.SpanStart));
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ParameterSyntax p:
                {
                    var mods = p.Modifiers;
                    for (int i = 0; i + 1 < mods.Count; i++)
                    {
                        if (mods[i].IsKind(SyntaxKind.RefKeyword) && mods[i + 1].IsKind(SyntaxKind.ReadOnlyKeyword))
                        {
                            Hit("C10", mods[i]);
                        }
                    }
                    if (p.Identifier.ValueText == "_" && !IsLambdaDiscardParameter(p))
                    {
                        Hit("C11", p.Identifier);
                    }
                    bool optAttr = p.AttributeLists.SelectMany(l => l.Attributes).Any(a => LastName(a.Name) is "Optional" or "OptionalAttribute" or "DefaultParameterValue" or "DefaultParameterValueAttribute");
                    if (p.Default is not null || optAttr)
                    {
                        Hit("C12", p.GetFirstToken());
                    }
                    break;
                }
                case VariableDeclaratorSyntax v when v.Identifier.ValueText == "_":
                    Hit("C11", v.Identifier);
                    break;
                case ForEachStatementSyntax f when f.Identifier.ValueText == "_":
                    Hit("C11", f.Identifier);
                    break;
                case SingleVariableDesignationSyntax d when d.Identifier.ValueText == "_":
                    Hit("C11", d.Identifier);
                    break;
                case LocalFunctionStatementSyntax lf when lf.Identifier.ValueText == "_":
                    Hit("C11", lf.Identifier);
                    break;
                case MethodDeclarationSyntax m when m.Identifier.ValueText == "_":
                    Hit("C11", m.Identifier);
                    break;
                case PropertyDeclarationSyntax pr when pr.Identifier.ValueText == "_":
                    Hit("C11", pr.Identifier);
                    break;
                case BaseTypeDeclarationSyntax td when td.Identifier.ValueText == "_":
                    Hit("C11", td.Identifier);
                    break;
                case EnumMemberDeclarationSyntax em when em.Identifier.ValueText == "_":
                    Hit("C11", em.Identifier);
                    break;
                case TypeParameterSyntax tp when tp.Identifier.ValueText == "_":
                    Hit("C11", tp.Identifier);
                    break;
                case LabeledStatementSyntax ls when ls.Identifier.ValueText == "_":
                    Hit("C11", ls.Identifier);
                    break;
                case DelegateDeclarationSyntax dd when dd.Identifier.ValueText == "_":
                    Hit("C11", dd.Identifier);
                    break;
                // decided by main: namespace `_`, using alias `_`, query range variables and tuple element names `_`
                // are C11 too (literal rule: any declared identifier `_` that C# does not treat as a discard)
                case BaseNamespaceDeclarationSyntax ns:
                    foreach (var part in ns.Name.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Where(n => n.Identifier.ValueText == "_"))
                    {
                        Hit("C11", part.Identifier);
                    }
                    break;
                case UsingDirectiveSyntax ud when ud.Alias is not null && ud.Alias.Name.Identifier.ValueText == "_":
                    Hit("C11", ud.Alias.Name.Identifier);
                    break;
                case FromClauseSyntax fc when fc.Identifier.ValueText == "_":
                    Hit("C11", fc.Identifier);
                    break;
                case LetClauseSyntax lc when lc.Identifier.ValueText == "_":
                    Hit("C11", lc.Identifier);
                    break;
                case JoinClauseSyntax jc when jc.Identifier.ValueText == "_":
                    Hit("C11", jc.Identifier);
                    break;
                case JoinIntoClauseSyntax ji when ji.Identifier.ValueText == "_":
                    Hit("C11", ji.Identifier);
                    break;
                case QueryContinuationSyntax qc when qc.Identifier.ValueText == "_":
                    Hit("C11", qc.Identifier);
                    break;
                case TupleElementSyntax te when te.Identifier.ValueText == "_":
                    Hit("C11", te.Identifier);
                    break;
                case ArgumentSyntax ta when ta.Parent is TupleExpressionSyntax && ta.NameColon is { } nc && nc.Name.Identifier.ValueText == "_":
                    Hit("C11", nc.Name.Identifier);
                    break;
                case ArgumentSyntax a when a.NameColon is not null:
                    if (NamedArgumentOutOfPosition(model, a))
                    {
                        Hit("C13", a.NameColon.Name.Identifier);
                    }
                    break;
                case AssignmentExpressionSyntax asg:
                {
                    var left = StripParens(asg.Left);
                    if (left is ThisExpressionSyntax th)
                    {
                        Hit("C14", th.Token);
                    }
                    else if (left is TupleExpressionSyntax tup)
                    {
                        foreach (var th2 in tup.DescendantNodes().OfType<ThisExpressionSyntax>().Where(t => t.Parent is ArgumentSyntax))
                        {
                            Hit("C14", th2.Token);
                        }
                    }
                    break;
                }
                case ArgumentSyntax ra when StripParens(ra.Expression) is ThisExpressionSyntax th3 && (ra.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || ra.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)):
                    Hit("C14", th3.Token);
                    break;
                case RefExpressionSyntax re when StripParens(re.Expression) is ThisExpressionSyntax th4 && !BoundAsReadonlyRef(re):
                    Hit("C14", th4.Token);
                    break;
                // decided by main: prefix/postfix `++`/`--` on `this` is an assignment to `this`
                case PrefixUnaryExpressionSyntax pre when pre.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression && StripParens(pre.Operand) is ThisExpressionSyntax th5:
                    Hit("C14", th5.Token);
                    break;
                case PostfixUnaryExpressionSyntax post when post.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression && StripParens(post.Operand) is ThisExpressionSyntax th6:
                    Hit("C14", th6.Token);
                    break;
                case ConditionalExpressionSyntax ce:
                    if (StripParens(ce.WhenTrue) is RefExpressionSyntax || StripParens(ce.WhenFalse) is RefExpressionSyntax)
                    {
                        Hit("C15", ce.QuestionToken);
                    }
                    if (IsConstant(model, ce.Condition))
                    {
                        Hit("C19", ce.Condition.GetFirstToken());
                    }
                    break;
                case CollectionExpressionSyntax coll when NestedOrBranch(coll):
                    Hit("C16", coll.OpenBracketToken);
                    break;
                case IdentifierNameSyntax id when IsNativeIntTypeName(model, id):
                    Hit("C17", id.Identifier);
                    break;
                case SwitchSectionSyntax sec:
                    foreach (var st in sec.Statements)
                    {
                        if (DeclaresIntoSwitchBlock(st))
                        {
                            Hit("C18", st.GetFirstToken());
                        }
                    }
                    break;
                // C21 (decided 2026-11-18): `: base(...)`, with or without arguments, on any constructor; anchor `base`
                // (C9 `: this(...)` is a declaration cut of its own)
                case ConstructorInitializerSyntax ci when ci.IsKind(SyntaxKind.BaseConstructorInitializer):
                    Hit("C21", ci.ThisOrBaseKeyword);
                    break;
                case IfStatementSyntax ifs when IsConstant(model, ifs.Condition):
                    Hit("C19", ifs.Condition.GetFirstToken());
                    break;
                case WhileStatementSyntax ws when !ws.Condition.IsKind(SyntaxKind.TrueLiteralExpression) && IsConstant(model, ws.Condition):
                    Hit("C19", ws.Condition.GetFirstToken());
                    break;
                case DoStatementSyntax ds when !ds.Condition.IsKind(SyntaxKind.TrueLiteralExpression) && IsConstant(model, ds.Condition):
                    Hit("C19", ds.Condition.GetFirstToken());
                    break;
                case ForStatementSyntax fs when fs.Condition is not null && !fs.Condition.IsKind(SyntaxKind.TrueLiteralExpression) && IsConstant(model, fs.Condition):
                    Hit("C19", fs.Condition.GetFirstToken());
                    break;
                case WhenClauseSyntax wc when IsConstant(model, wc.Condition):
                    Hit("C19", wc.Condition.GetFirstToken());
                    break;
                case SwitchStatementSyntax ss when IsConstant(model, ss.Expression):
                    Hit("C19", ss.Expression.GetFirstToken());
                    break;
                case SwitchExpressionSyntax se when IsConstant(model, se.GoverningExpression):
                    Hit("C19", se.GoverningExpression.GetFirstToken());
                    break;
                case RelationalPatternSyntax rp:
                    Hit("C20", rp.OperatorToken);
                    break;
                case BinaryPatternSyntax bp when bp.IsKind(SyntaxKind.AndPattern):
                    Hit("C20", bp.OperatorToken);
                    break;
            }
        }
        var res = new List<CutHit>();
        foreach (var g in all.GroupBy(h => h.Cut).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int pos = g.Min(h => h.Pos);
            var lp = tree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(pos, 0)).StartLinePosition;
            var c = Cuts.First(x => x.Cut == g.Key);
            res.Add(new CutHit(c.Tag, c.Cut, lp.Line + 1, lp.Character + 1, c.Message));
        }
        return res;
    }

    private static string LastName(NameSyntax n) => n switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
        SimpleNameSyntax s => s.Identifier.ValueText,
        _ => "",
    };

    private static ExpressionSyntax StripParens(ExpressionSyntax e)
    {
        while (e is ParenthesizedExpressionSyntax p)
        {
            e = p.Expression;
        }
        return e;
    }

    /// <summary>C# treats `_` as a discard only for lambda parameters when more than one parameter is named `_`.</summary>
    private static bool IsLambdaDiscardParameter(ParameterSyntax p) =>
        p.Parent is ParameterListSyntax { Parent: ParenthesizedLambdaExpressionSyntax } list && list.Parameters.Count(x => x.Identifier.ValueText == "_") > 1;

    private static bool IsConstant(SemanticModel model, ExpressionSyntax e) => model.GetConstantValue(e).HasValue;

    private static bool NamedArgumentOutOfPosition(SemanticModel model, ArgumentSyntax a)
    {
        if (a.Parent is not BaseArgumentListSyntax list || a.NameColon is null)
        {
            return false;
        }
        int index = list.Arguments.IndexOf(a);
        var sym = model.GetSymbolInfo(list.Parent!).Symbol ?? model.GetSymbolInfo(list.Parent!).CandidateSymbols.FirstOrDefault();
        var ps = sym switch
        {
            IMethodSymbol m => m.Parameters,
            IPropertySymbol p => p.Parameters,
            _ => default,
        };
        if (ps.IsDefault)
        {
            return false;
        }
        string name = a.NameColon.Name.Identifier.ValueText;
        int ordinal = ps.IndexOf(ps.FirstOrDefault(p => p.Name == name)!);
        return ordinal >= 0 && ordinal != index;
    }

    /// <summary>`ref this` bound to a `ref readonly` local is not a writable ref (C14 covers writable binds only).</summary>
    private static bool BoundAsReadonlyRef(RefExpressionSyntax re) =>
        re.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Type: RefTypeSyntax rt } } } && rt.ReadOnlyKeyword.IsKind(SyntaxKind.ReadOnlyKeyword);

    private static bool NestedOrBranch(CollectionExpressionSyntax coll)
    {
        SyntaxNode child = coll;
        var p = coll.Parent;
        while (p is ParenthesizedExpressionSyntax)
        {
            child = p;
            p = p.Parent;
        }
        return p switch
        {
            ExpressionElementSyntax or SpreadElementSyntax => true,
            ConditionalExpressionSyntax ce => ce.WhenTrue == child || ce.WhenFalse == child,
            SwitchExpressionArmSyntax arm => arm.Expression == child,
            _ => false,
        };
    }

    private static bool IsNativeIntTypeName(SemanticModel model, IdentifierNameSyntax id)
    {
        if (id.Identifier.ValueText is not ("nint" or "nuint" or "IntPtr" or "UIntPtr"))
        {
            return false;
        }
        var sym = model.GetSymbolInfo(id).Symbol;
        return sym is INamedTypeSymbol { SpecialType: SpecialType.System_IntPtr or SpecialType.System_UIntPtr };
    }

    /// <summary>
    /// C18: a statement directly in a switch section that declares a local scoped to the switch block: a local
    /// declaration, or an expression variable (out var, pattern or deconstruction designation) in an expression
    /// statement, an `if` condition, `return`, `throw`, `yield return` or a nested switch statement's governing
    /// expression. A local function directly in a section is C18 too (it also reports C1,
    /// `cut.localfunc-in-switch-section`; decided by main).
    /// </summary>
    private static bool DeclaresIntoSwitchBlock(StatementSyntax st)
    {
        ExpressionSyntax? e = st switch
        {
            LocalDeclarationStatementSyntax => null,
            ExpressionStatementSyntax es => es.Expression,
            IfStatementSyntax ifs => ifs.Condition,
            ReturnStatementSyntax rs => rs.Expression,
            ThrowStatementSyntax ts => ts.Expression,
            YieldStatementSyntax ys => ys.Expression,
            SwitchStatementSyntax ss => ss.Expression,
            _ => null,
        };
        if (st is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax)
        {
            return true;
        }
        return e is not null && DeclaresExpressionVariable(e);
    }

    private static bool DeclaresExpressionVariable(ExpressionSyntax e) =>
        e.DescendantNodesAndSelf(n => n is not (AnonymousFunctionExpressionSyntax or SwitchExpressionSyntax))
            .Any(n => n is SingleVariableDesignationSyntax);
}
