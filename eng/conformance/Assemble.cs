using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>A generated program plus its provenance.</summary>
internal sealed record GenProgram(ProgramNode Ast, string Plan, string[] Mutations);

internal sealed partial class Gen
{
    /// <summary>Generates one program: anchor <paramref name="outer"/> with <paramref name="inner"/> nested inside.</summary>
    public GenProgram Generate(Anchor outer, Anchor inner, Anchor? third)
    {
        _container = _r.Chance(0.3) ? "C" : "top";
        var paramTys = new List<(Ty T, string N)> { (Ty.Int, "p0"), (Ty.Bool, "p1") };
        if (_r.Chance(0.3))
        {
            paramTys.Add((Ty.Long, "p2"));
        }
        string fname = _container == "top" ? "F" : "M";
        var saveScope = _scope;
        _fn = new FnState(Ty.Int, _container) { InStaticLocal = _container == "top", Acc = "acc" };
        _scope = new Scope(_container == "top" ? _topScope : ClassScope(_container), staticBoundary: _container == "top");
        foreach (var (t, n) in paramTys)
        {
            Declare(n, t, VK.Param);
        }
        var body = new List<Stmt>();
        Declare("acc", Ty.Int, VK.Local);
        body.Add(new LocalDeclStmt(Ty.Int, "acc", new NameExpr("p0") { Type = Ty.Int }, ""));
        Inner innermost = third is { } th ? () => Run(th, FillerInner) : FillerInner;
        _topPending = true;
        body.AddRange(Run(outer, () => Run(inner, innermost)));
        _topPending = false;
        bool endsWithReturnSwitch = body.Count > 0 && body[^1] is SwitchStmt s0 && s0.Sections.All(s => s.Stmts.Count > 0 && s.Stmts[^1] is ReturnStmt);
        if (!endsWithReturnSwitch && _r.Chance(0.3))
        {
            body.AddRange(Filler(1));
        }
        bool omitReturn = body.Count > 0 && body[^1] is SwitchStmt sw && sw.Sections.All(s => s.Stmts.Count > 0 && s.Stmts[^1] is ReturnStmt) && _r.Chance(0.6);
        if (!omitReturn)
        {
            body.Add(new ReturnStmt(new NameExpr("acc") { Type = Ty.Int }));
        }
        _scope = saveScope;
        var mainFn = new FuncDecl(_container == "top" ? "static" : "public static", Ty.Int, fname,
            new ParamList([.. paramTys.Select(p => new ParamDecl(p.T, p.N, ""))]), new Block(body), null, _container == "top");

        // top-level statements: two calls with different arguments
        var top = new List<Stmt>();
        string callee = _container == "top" ? fname : _container + "." + fname;
        int[] a0 = [_r.Range(-2, 6), _r.Range(-3, 9)];
        for (int i = 0; i < 2; i++)
        {
            var args = new List<Arg> { new("", new Lit(a0[i].ToString(System.Globalization.CultureInfo.InvariantCulture), Ty.Int)), new("", new Lit(i == 0 ? "true" : "false", Ty.Bool)) };
            if (paramTys.Count > 2)
            {
                args.Add(new Arg("", new Lit((i * 7 - 2) + "L", Ty.Long)));
            }
            top.Add(new ExprStmt(new CallExpr(null, "WriteLine", [new Arg("", new CallExpr(null, callee, args) { Type = Ty.Int })])));
        }
        foreach (var v in _topScope.Vars.Where(v => v.Kind == VK.Const && !v.Name.Contains('.', StringComparison.Ordinal)))
        {
            _ = v;
        }
        if (_container == "top")
        {
            top.Add(new LocalFuncStmt(mainFn));
            top.AddRange(_topHelpers);
        }
        else
        {
            var members = new List<Node>();
            members.AddRange(ClassMembers(_container).OfType<FieldDecl>());
            members.Add(mainFn);
            members.AddRange(ClassMembers(_container).Where(m => m is not FieldDecl));
            _types.Insert(0, new ClassDecl(_container, "static", null, null, members));
        }
        foreach (var b in _pendingTopStmts)
        {
            top.Insert(0, b);
        }
        foreach (var d in _pendingDirectives)
        {
            top.Insert(0, d);
        }
        ActivationProgramOptions(top);
        var prog = (ProgramNode)TreeOps.Rewrite(new ProgramNode(new Block(top), new TypeList([.. _types, .. _pendingTypes])), n => DecutSection(Hygiene(n)));
        var muts = new List<string>();
        // the soundness oracle needs accepted programs: near-miss mutants (mostly C#-invalid) are rarer there
        if (_r.Chance(Profile == "soundness" ? SoundnessMutationRate : MutationRate))
        {
            var (p2, m) = Mutator.Mutate(prog, _r);
            if (m is not null)
            {
                // a mutation may move a declaration into a switch section (dup-name, scope-leak): keep C18 out
                prog = (ProgramNode)TreeOps.Rewrite(p2, DecutSection);
                muts.Add(m);
            }
        }
        return new GenProgram(prog, $"{outer}>{inner}" + (third is null ? "" : ">" + third), [.. muts]);
    }

    public double MutationRate { get; init; } = 0.3;

    /// <summary>Near-miss mutation rate of the soundness profile.</summary>
    public const double SoundnessMutationRate = 0.1;

    private static Expr Bare(Expr e) => e is ParenExpr p ? Bare(p.E) : e;

    /// <summary>
    /// Removes accidental C# warning shapes that would turn a valid program into a TreatWarningsAsErrors
    /// failure without testing anything: self-assignment (CS1717) and self-comparison (CS1718).
    /// </summary>
    private static Node Hygiene(Node n)
    {
        switch (n)
        {
            case AssignStmt { Op: "=" } a when Bare(a.Value) is NameExpr v && a.Target is NameExpr t && v.Name == t.Name:
                return t.Type == Ty.Bool
                    ? new AssignStmt(a.Target, "=", new UnExpr("!", a.Value).Typed<UnExpr>(Ty.Bool))
                    : new AssignStmt(a.Target, "+=", a.Value);
            case BinExpr { Op: "==" or "!=" or "<" or ">" or "<=" or ">=" } b
                when Bare(b.L) is NameExpr l && Bare(b.R) is NameExpr r && l.Name == r.Name && l.Type is { } lt:
                return lt == Ty.Bool
                    ? new BinExpr(b.Op, b.L, new Lit("false", Ty.Bool)) { Type = b.Type }
                    : lt.IsNumeric && lt.Kind is not TK.Char
                        ? new BinExpr(b.Op, b.L, new Lit(lt.Kind switch { TK.Long => "1L", TK.Double => "1.0", TK.Float => "1f", TK.UInt => "1u", TK.ULong => "1UL", _ => "1" }, lt)) { Type = b.Type }
                        : n;
            default:
                return n;
        }
    }

    /// <summary>
    /// Cut C18: a statement directly in a switch section may not declare a local scoped to the switch block. A section
    /// whose statements declare one (a local declaration, an out/pattern variable in a statement or `if` condition, or a
    /// raw template statement) is wrapped in one block, which keeps its meaning (section locals were never shared:
    /// the generator scopes them per section).
    /// </summary>
    private static Node DecutSection(Node n)
    {
        if (n is SwitchSection sec && sec.Stmts.Any(st => st is LocalDeclStmt or RawStmt || SwitchStmt.SectionLevelExprVars(st).Any()))
        {
            return new SwitchSection(sec.Labels, [new Block([.. sec.Stmts])]);
        }
        return n;
    }

    private readonly List<Stmt> _pendingTopStmts = [];
    private readonly List<Node> _pendingTypes = [];

    // ───────────── banned-by-design constructs (SPEC §1 closed subset, §2, §4) ─────────────

    private List<Stmt> PBanned(Inner inner)
    {
        var res = inner();
        string p = ReadableV(v => v.Type == Ty.Int && v.Kind is not (VK.Const or VK.EnumMember)).Select(v => v.Access).FirstOrDefault() ?? "1";
        int k = _r.Int(20);
        switch (k)
        {
            case 0:
                res.Add(new RawStmt($"int? nq = {p} > 0 ? {p} : null;", ["banned.nullable", "banned.null"]));
                res.Add(new RawStmt("WriteLine(nq ?? 0);", []));
                break;
            case 1:
                res.Add(new RawStmt("string? ns = \"a\";", ["banned.nullable"]));
                res.Add(new RawStmt("WriteLine(ns!);", ["banned.null-forgiving"]));
                break;
            case 2:
                res.Add(new RawStmt($"if ({p} > 100) goto Skip{_names};", ["banned.goto"]));
                res.Add(new RawStmt($"WriteLine({p});", []));
                res.Add(new RawStmt($"Skip{_names}: WriteLine(0);", ["banned.goto"]));
                break;
            case 3:
                res.Add(new RawStmt("string st = \"a\";", []));
                res.Add(new RawStmt("string su = st + \"b\";", ["banned.string-concat"]));
                res.Add(new RawStmt("WriteLine(su);", []));
                break;
            case 4:
                res.Add(new RawStmt("string st = \"a\";", []));
                res.Add(new RawStmt("WriteLine(st == \"a\");", ["banned.string-equality"]));
                break;
            case 5:
                res.Add(new RawStmt("string st = \"abc\";", []));
                res.Add(new RawStmt("WriteLine(st.Length);", ["banned.string-length"]));
                break;
            case 6:
                res.Add(new RawStmt($"WriteLine($\"v={{{p}}}\");", ["banned.interpolation"]));
                break;
            case 7:
                _pendingTypes.Add(new RawStmt("public struct Dsp : IDisposable { public int X; public void Dispose() => WriteLine(X); }", ["banned.struct-idisposable"]));
                res.Add(new Block([new RawStmt("using var dsp = new Dsp();", ["using.var"]), new RawStmt("WriteLine(1);", [])]));
                break;
            case 8:
                EnsureResource();
                res.Add(new RawStmt("var md = new Res(5);", []));
                res.Add(new RawStmt("md.Dispose();", ["banned.manual-dispose"]));
                break;
            case 9:
                EnsureResource();
                _pendingTypes.Add(new RawStmt("public sealed class Holder(Res r) { public Res R = r; }", ["banned.linear-field"]));
                res.Add(new RawStmt("var hold = new Holder(new Res(3));", []));
                res.Add(new RawStmt("WriteLine(hold.R.Id);", []));
                break;
            case 10:
                res.Add(new RawStmt("try { WriteLine(1); } finally { WriteLine(2); }", ["banned.try"]));
                break;
            case 11:
                res.Add(new RawStmt($"if ({p} > 1000) throw new InvalidOperationException();", ["banned.throw"]));
                break;
            case 12:
                res.Add(new RawStmt("Func<int, int> lam = z => z + 1;", ["banned.lambda"]));
                res.Add(new RawStmt($"WriteLine(lam({p}));", []));
                break;
            case 13:
                if (_container == "top" && p.StartsWith('p'))
                {
                    res.Add(new RawStmt($"int Capt(int z) => z + {p};", ["banned.nonstatic-localfunc"]));
                    res.Add(new RawStmt("WriteLine(Capt(2));", []));
                }
                else
                {
                    res.Add(new RawStmt("int Capt(int z) => z + 1;", ["banned.nonstatic-localfunc"]));
                    res.Add(new RawStmt("WriteLine(Capt(2));", []));
                }
                break;
            case 14:
                _pendingTypes.Add(new RawStmt("public static class Glob { public static int Count; }", ["banned.static-field"]));
                res.Add(new RawStmt($"Glob.Count = {p};", []));
                res.Add(new RawStmt("WriteLine(Glob.Count);", []));
                break;
            case 15:
                res.Add(new RawStmt($"int[] arr = [{p}, 2];", ["banned.array"]));
                res.Add(new RawStmt("WriteLine(arr[0]);", []));
                break;
            case 16:
                res.Add(new RawStmt($"object ob = {p};", ["banned.object"]));
                res.Add(new RawStmt("WriteLine(ob is int);", []));
                break;
            case 17:
                res.Add(new RawStmt($"WriteLine(checked({p} + 1));", ["banned.checked"]));
                break;
            case 18:
                _pendingTypes.Add(new RawStmt("public class BaseK { public int V = 1; }", ["banned.inheritance"]));
                _pendingTypes.Add(new RawStmt("public sealed class DerK : BaseK { }", ["banned.inheritance"]));
                res.Add(new RawStmt("WriteLine(new DerK().V);", []));
                break;
            default:
                res.Add(new RawStmt("Vec<int> dv = default;", ["banned.default-nonzerosafe"]));
                res.Add(new RawStmt("WriteLine(dv.Count);", []));
                break;
        }
        return res;
    }
}
