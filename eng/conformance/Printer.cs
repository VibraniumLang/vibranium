using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Vibranium.Conformance;

/// <summary>Deterministic C# printer for the generator AST.</summary>
internal static class Printer
{
    public static string Print(ProgramNode p)
    {
        var sb = new StringBuilder();
        foreach (var s in p.Top.Items)
        {
            Stmt(sb, s, 0);
        }
        foreach (var t in p.Types.Items)
        {
            TypeDecl(sb, t);
        }
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, int indent, string text) => sb.Append(' ', indent * 4).Append(text).Append('\n');

    private static void TypeDecl(StringBuilder sb, Node t)
    {
        switch (t)
        {
            case EnumDecl e:
                Line(sb, 0, $"public enum {e.Name} {{ {string.Join(", ", e.Members.Select(m => $"{m.Name} = {m.Value}"))} }}");
                break;
            case StructDecl s:
                Line(sb, 0, $"public {(s.Mods.Length > 0 ? s.Mods + " " : "")}struct {s.Name}{(s.PrimaryCtor is null ? "" : "(" + s.PrimaryCtor + ")")}{(s.Bases is null ? "" : " : " + s.Bases)}");
                Line(sb, 0, "{");
                foreach (var m in s.Members)
                {
                    Member(sb, m, 1);
                }
                Line(sb, 0, "}");
                break;
            case ClassDecl c:
                Line(sb, 0, $"public {(c.Mods.Length > 0 ? c.Mods + " " : "")}class {c.Name}{(c.PrimaryCtor is null ? "" : "(" + c.PrimaryCtor + ")")}{(c.Bases is null ? "" : " : " + c.Bases)}");
                Line(sb, 0, "{");
                foreach (var m in c.Members)
                {
                    Member(sb, m, 1);
                }
                Line(sb, 0, "}");
                break;
            case RawStmt r:
                Line(sb, 0, r.Text);
                break;
            default:
                throw new InvalidOperationException("unknown type decl " + t.GetType().Name);
        }
    }

    private static void Member(StringBuilder sb, Node m, int ind)
    {
        switch (m)
        {
            case FieldDecl f:
                Line(sb, ind, $"{f.Mods} {f.Type.Cs} {f.Name}{(f.Init is null ? "" : " = " + Expr(f.Init))};");
                break;
            case FuncDecl fn:
                Func(sb, fn, ind);
                break;
            case RawStmt r:
                Line(sb, ind, r.Text);
                break;
            default:
                throw new InvalidOperationException("unknown member " + m.GetType().Name);
        }
    }

    private static string ParamText(ParamDecl p) => (p.Mod.Length > 0 ? p.Mod + " " : "") + p.Type.Cs + " " + p.Name;

    private static void Func(StringBuilder sb, FuncDecl f, int ind)
    {
        string head = $"{(f.Mods.Length > 0 ? f.Mods + " " : "")}{f.Ret.Cs} {f.Name}({string.Join(", ", f.Params.Items.Select(ParamText))})";
        if (f.ExprBody is not null)
        {
            Line(sb, ind, head + " => " + Expr(f.ExprBody) + ";");
            return;
        }
        var body = f.Body ?? new Block([]);
        if (body.Items.Count == 1 && body.Items[0] is ReturnStmt { E: not null } r1 && Expr(r1.E!).Length < 40)
        {
            Line(sb, ind, head + " { return " + Expr(r1.E!) + "; }");
            return;
        }
        Line(sb, ind, head);
        Line(sb, ind, "{");
        foreach (var s in body.Items)
        {
            Stmt(sb, s, ind + 1);
        }
        Line(sb, ind, "}");
    }

    /// <summary>Prints an embedded statement (if/loop body) honoring blocks.</summary>
    private static void Embedded(StringBuilder sb, string head, Stmt body, int ind)
    {
        if (body is Block b)
        {
            if (b.Items.Count == 0)
            {
                Line(sb, ind, head + " { }");
                return;
            }
            Line(sb, ind, head);
            Line(sb, ind, "{");
            foreach (var s in b.Items)
            {
                Stmt(sb, s, ind + 1);
            }
            Line(sb, ind, "}");
            return;
        }
        string inner = OneLine(body);
        if (inner.Length > 0)
        {
            Line(sb, ind, head + " " + inner);
            return;
        }
        Line(sb, ind, head);
        Stmt(sb, body, ind + 1);
    }

    /// <summary>Single-line rendering of simple statements, or "" when multi-line.</summary>
    private static string OneLine(Stmt s) => s switch
    {
        ExprStmt or AssignStmt or ReturnStmt or BreakStmt or ContinueStmt or LocalDeclStmt or RawStmt => StmtText(s),
        _ => "",
    };

    private static string StmtText(Stmt s) => s switch
    {
        ExprStmt e => Expr(e.E) + ";",
        AssignStmt a => AssignText(a) + ";",
        ReturnStmt r => r.E is null ? "return;" : "return " + Expr(r.E) + ";",
        BreakStmt => "break;",
        ContinueStmt => "continue;",
        LocalDeclStmt d => DeclText(d) + ";",
        RawStmt r => r.Text,
        _ => throw new InvalidOperationException(),
    };

    private static string AssignText(AssignStmt a) => $"{Expr(a.Target)} {a.Op} {Expr(a.Value)}";

    private static string DeclText(LocalDeclStmt d)
    {
        string type = d.Type?.Cs ?? "var";
        string mods = d.Mods.Length > 0 ? d.Mods + " " : "";
        string init = d.Init is null ? "" : (d.Mods.StartsWith("ref", StringComparison.Ordinal) ? " = ref " : " = ") + Expr(d.Init);
        return $"{mods}{type} {d.Name}{init}";
    }

    public static void Stmt(StringBuilder sb, Stmt s, int ind)
    {
        switch (s)
        {
            case Block b:
                if (b.Items.Count == 0)
                {
                    Line(sb, ind, "{ }");
                    break;
                }
                Line(sb, ind, "{");
                foreach (var x in b.Items)
                {
                    Stmt(sb, x, ind + 1);
                }
                Line(sb, ind, "}");
                break;
            case IfStmt i:
                Embedded(sb, $"if ({Expr(i.Cond)})", i.Then, ind);
                if (i.Else is not null)
                {
                    if (i.Else is IfStmt)
                    {
                        Line(sb, ind, "else");
                        Stmt(sb, i.Else, ind + 1);
                    }
                    else
                    {
                        Embedded(sb, "else", i.Else, ind);
                    }
                }
                break;
            case WhileStmt w:
                Embedded(sb, $"while ({Expr(w.Cond)})", w.Body, ind);
                break;
            case DoStmt d:
                Embedded(sb, "do", d.Body, ind);
                Line(sb, ind, $"while ({Expr(d.Cond)});");
                break;
            case ForStmt f:
                Embedded(sb, $"for ({(f.Init is null ? "" : DeclText(f.Init))}; {(f.Cond is null ? "" : Expr(f.Cond))}; {(f.Iter is null ? "" : AssignText(f.Iter))})", f.Body, ind);
                break;
            case ForeachStmt fe:
                Embedded(sb, $"foreach ({(fe.IsRef ? "ref " : "")}{fe.Type?.Cs ?? "var"} {fe.Name} in {Expr(fe.Coll)})", fe.Body, ind);
                break;
            case SwitchStmt sw:
                Line(sb, ind, $"switch ({Expr(sw.Sel)})");
                Line(sb, ind, "{");
                foreach (var sec in sw.Sections)
                {
                    string labels = string.Join(" ", sec.Labels.Items.Select(LabelText));
                    var simple = sec.Stmts.Select(OneLine).ToList();
                    if (sec.Stmts.Count > 0 && simple.All(x => x.Length > 0) && simple.Sum(x => x.Length) + labels.Length < 90)
                    {
                        Line(sb, ind + 1, labels + " " + string.Join(" ", simple));
                    }
                    else
                    {
                        Line(sb, ind + 1, labels);
                        foreach (var x in sec.Stmts)
                        {
                            Stmt(sb, x, ind + 2);
                        }
                    }
                }
                Line(sb, ind, "}");
                break;
            case LocalFuncStmt lf:
                Func(sb, lf.Func, ind);
                break;
            case UsingBlockStmt u:
                Embedded(sb, $"using (var {u.Name} = {Expr(u.Init)})", u.Body, ind);
                break;
            default:
                Line(sb, ind, StmtText(s));
                break;
        }
    }

    private static string LabelText(CaseLabel l) =>
        (l.Pat is null ? "default" : "case " + Pat(l.Pat)) + (l.Guard is null ? "" : " when " + Expr(l.Guard)) + ":";

    public static string Pat(Pat p) => p switch
    {
        ConstPat c => Expr(c.E),
        RelPat r => r.Op + " " + Expr(r.E),
        DeclPat d => d.Type.Cs + " " + d.Name,
        VarPat v => "var " + v.Name,
        DiscardPat => "_",
        TypePat t => t.TypeText,
        UnionPat u => u.CaseType + "(" + Pat(u.Inner) + ")",
        NotPat n => "not " + (n.P is BinPat ? "(" + Pat(n.P) + ")" : Pat(n.P)),
        BinPat b => Pat(b.L) + " " + b.Op + " " + Pat(b.R),
        _ => throw new InvalidOperationException(p.GetType().Name),
    };

    private static string ArgText(Arg a) => (a.Mod.Length > 0 ? a.Mod + " " : "") + Expr(a.E);

    public static string Expr(Expr e) => e switch
    {
        Lit l => l.Text,
        NameExpr n => n.Name,
        BinExpr b => Expr(b.L) + " " + b.Op + " " + Expr(b.R),
        UnExpr u => u.Op + Expr(u.E),
        CondExpr c => c.Compact ? $"{Expr(c.C)}?{Expr(c.A)}:{Expr(c.B)}" : $"{Expr(c.C)} ? {Expr(c.A)} : {Expr(c.B)}",
        CastExpr c => $"({c.To.Cs}){Expr(c.E)}",
        ParenExpr p => "(" + Expr(p.E) + ")",
        OutDeclExpr o => o.Name == "_" && o.DeclType is null ? "var _" : (o.DeclType?.Cs ?? "var") + " " + o.Name,
        CallExpr c => (c.Receiver is null ? "" : Expr(c.Receiver) + ".") + c.Callee + (c.Callee is "None" or "IsSome" or "IsEmpty" or "IsOk" ? "" : "(" + string.Join(", ", c.Args.Select(ArgText)) + ")"),
        MemberExpr m => Expr(m.E) + "." + m.Name,
        IndexExpr i => Expr(i.E) + "[" + Expr(i.Index) + "]",
        CollExpr c => "[" + string.Join(", ", c.Elems.Select(Expr)) + "]",
        SwitchExpr s => Expr(s.Sel) + " switch { " + string.Join(", ", s.Arms.Select(a => Pat(a.Pat) + (a.Guard is null ? "" : " when " + Expr(a.Guard)) + " => " + Expr(a.Value))) + " }",
        IsPatExpr i => Expr(i.E) + " is " + Pat(i.Pat),
        NewExpr n => "new " + n.TypeText + "(" + string.Join(", ", n.Args.Select(ArgText)) + ")",
        RawExpr r => r.Text,
        _ => throw new InvalidOperationException(e.GetType().Name),
    };
}

/// <summary>Collects feature tags over a program.</summary>
internal static class Tagger
{
    public static SortedSet<string> Collect(ProgramNode p)
    {
        var tags = new SortedSet<string>(StringComparer.Ordinal);
        var stack = new List<Node>();
        Walk(p, stack, tags);
        return tags;
    }

    // Cut rules are flow rules: every frontend applies them only where the code can run, so a `cut.*` tag is not
    // emitted for code that Roslyn proves unreachable (after return/break/continue, a `when false` section, a
    // section of a switch on a constant that none of its labels can match, the body of `if (false)`) or that follows
    // an unconditional Panic (runtime-unreachable, as for VIB007). The same holds for vib-reject expectations. The
    // declaration cuts C5–C9 are syntactic (one VIB000 per file at the first occurrence), so they are kept in dead code.
    private static void Walk(Node n, List<Node> stack, SortedSet<string> tags, bool dead = false)
    {
        foreach (var t in n.Tags(new NodeCtx(stack)))
        {
            if (!(dead && ((t.StartsWith("cut.", StringComparison.Ordinal) && !Features.DeclarationCuts.Contains(t)) || Features.ExpectedReject.ContainsKey(t))))
            {
                tags.Add(t);
            }
        }
        stack.Add(n);
        bool after = false;
        bool sectionDead = n is SwitchSection sec && sec.Labels.Items.Count > 0
            && (sec.Labels.Items.All(l => l.Guard is Lit { Text: "false" })
                || (stack.Count > 1 && stack[^2] is SwitchStmt sw && ConstantSwitch.Unreachable(sw, sec, stack[0])));
        foreach (var k in n.Kids)
        {
            if (k is not null)
            {
                bool kidDead = dead || after
                    || (sectionDead && k is Stmt)
                    || (n is IfStmt { Cond: Lit { Text: "false" } } ifs && ReferenceEquals(k, ifs.Then));
                Walk(k, stack, tags, kidDead);
                if (n is Block or SwitchSection && k is ReturnStmt or BreakStmt or ContinueStmt or ExprStmt { E: CallExpr { Callee: "VibraniumRuntime.Panic" } })
                {
                    after = true;
                }
            }
        }
        stack.RemoveAt(stack.Count - 1);
    }

    /// <summary>Counts AST nodes (used to report reduction ratios).</summary>
    public static int Size(Node n) => 1 + n.Kids.Sum(k => k is null ? 0 : Size(k));
}

/// <summary>
/// Reachability of the sections of a switch statement whose selector is a constant (an enum member, a const local or
/// field, or a literal), as Roslyn decides it (CS0162): case labels are tried in order, a section is reachable when
/// one of its labels may match and no earlier label matches for certain, and <c>default</c> is reachable when no
/// label matches for certain. Anything this evaluator cannot decide counts as reachable.
/// </summary>
internal static class ConstantSwitch
{
    public static bool Unreachable(SwitchStmt sw, SwitchSection target, Node root)
    {
        if (Eval(sw.Sel, root, 0) is not double v)
        {
            return false;
        }
        bool matched = false;
        bool reachable = false;
        foreach (var s in sw.Sections)
        {
            foreach (var l in s.Labels.Items)
            {
                if (l.Pat is null)
                {
                    continue;
                }
                bool? m = l.Guard is Lit { Text: "false" } ? false : Match(l.Pat, v, root);
                if (!matched && m != false && ReferenceEquals(s, target))
                {
                    reachable = true;
                }
                if (m == true && l.Guard is null or Lit { Text: "true" })
                {
                    matched = true;
                }
            }
        }
        if (!matched && target.Labels.Items.Any(l => l.Pat is null))
        {
            reachable = true;
        }
        return !reachable;
    }

    private static bool? Match(Pat p, double v, Node root) => p switch
    {
        ConstPat c => Eval(c.E, root, 0) is double k ? k == v : null,
        RelPat r => Eval(r.E, root, 0) is double k
            ? r.Op switch { "<" => v < k, "<=" => v <= k, ">" => v > k, ">=" => v >= k, _ => null }
            : null,
        DiscardPat or VarPat or DeclPat => true,
        NotPat n => Match(n.P, v, root) is bool b ? !b : null,
        BinPat { Op: "and" } b => And(Match(b.L, v, root), Match(b.R, v, root)),
        BinPat { Op: "or" } b => Or(Match(b.L, v, root), Match(b.R, v, root)),
        _ => null,
    };

    private static bool? And(bool? a, bool? b) => a == false || b == false ? false : a == true && b == true ? true : null;

    private static bool? Or(bool? a, bool? b) => a == true || b == true ? true : a == false && b == false ? false : null;

    private static double? Eval(Expr e, Node root, int depth)
    {
        if (depth > 4)
        {
            return null;
        }
        switch (e)
        {
            case Lit l:
                return ParseLiteral(l.Text);
            case ParenExpr pe:
                return Eval(pe.E, root, depth + 1);
            case CastExpr ce:
                return Eval(ce.E, root, depth + 1);
            case UnExpr { Op: "-" } ue:
                return -Eval(ue.E, root, depth + 1);
            case UnExpr { Op: "+" } ue:
                return Eval(ue.E, root, depth + 1);
            case MemberExpr { E: NameExpr owner } me:
                return EnumMember(owner.Name, me.Name, root) ?? Const(me.Name, root, depth);
            case NameExpr ne:
                int dot = ne.Name.LastIndexOf('.');
                return dot < 0 ? Const(ne.Name, root, depth) : EnumMember(ne.Name[..dot], ne.Name[(dot + 1)..], root) ?? Const(ne.Name[(dot + 1)..], root, depth);
            default:
                return null;
        }
    }

    private static double? EnumMember(string type, string member, Node root)
    {
        foreach (var en in All(root).OfType<EnumDecl>().Where(d => d.Name == type))
        {
            foreach (var (name, value) in en.Members)
            {
                if (name == member)
                {
                    return value;
                }
            }
        }
        return null;
    }

    // a const local or field; null unless every declaration of that name has the same known value
    private static double? Const(string name, Node root, int depth)
    {
        var inits = All(root).Select(n => n switch
        {
            LocalDeclStmt { Mods: "const" } ld when ld.Name == name => ld.Init,
            FieldDecl { IsConst: true } fd when fd.Name == name => fd.Init,
            _ => null,
        }).Where(i => i is not null).ToList();
        if (inits.Count == 0)
        {
            return null;
        }
        var values = inits.Select(i => Eval(i!, root, depth + 1)).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }

    private static double? ParseLiteral(string text)
    {
        string t = text.Trim();
        switch (t)
        {
            case "true":
                return 1;
            case "false":
                return 0;
        }
        // generator literals: 3, 3L, 3u, 3UL, 3.5f, 3.25, 'a', (ushort)3, (short)-2, (nint)(-2)
        while (t.StartsWith('('))
        {
            int close = t.IndexOf(')', StringComparison.Ordinal);
            if (close < 0)
            {
                return null;
            }
            string head = t[1..close];
            string rest = t[(close + 1)..];
            if (rest.Length == 0)
            {
                t = head; // a parenthesized value such as (-2)
                break;
            }
            t = rest; // a cast prefix
        }
        if (t.Length == 3 && t[0] == '\'' && t[2] == '\'')
        {
            return t[1];
        }
        t = t.TrimEnd('u', 'U', 'l', 'L', 'f', 'F', 'm', 'M', 'd', 'D');
        return double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : null;
    }

    private static IEnumerable<Node> All(Node n)
    {
        yield return n;
        foreach (var k in n.Kids)
        {
            if (k is not null)
            {
                foreach (var d in All(k))
                {
                    yield return d;
                }
            }
        }
    }
}
