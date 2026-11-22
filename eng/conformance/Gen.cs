using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

internal enum VK { Local, Param, Const, Field, EnumMember, Pattern, Out }

/// <summary>A variable known to the generator, with a conservative definite-assignment flag.</summary>
internal sealed class Var(string name, Ty type, VK kind)
{
    public string Name { get; } = name;
    public Ty Type { get; } = type;
    public VK Kind { get; } = kind;
    public bool Assigned { get; set; } = true;
    public bool Mutable { get; set; } = true;
    /// <summary>Visible through a static local function boundary (consts, enum members).</summary>
    public bool Static { get; set; }
    /// <summary>Text used to reference it (qualified names for class consts outside the class).</summary>
    public string Access { get; set; } = name;
    public bool IsRefLike => Type.Kind is TK.Span;
}

internal sealed class Scope(Scope? parent, bool staticBoundary = false, bool isLoop = false)
{
    public Scope? Parent { get; } = parent;
    public bool StaticBoundary { get; } = staticBoundary;
    public bool IsLoop { get; } = isLoop;
    public List<Var> Vars { get; } = [];
}

/// <summary>A callable function signature.</summary>
internal sealed record FuncSig(string Name, Ty Ret, (Ty Type, string Mod)[] Params, string Container);

/// <summary>Per-function generation state.</summary>
internal sealed class FnState(Ty ret, string container)
{
    public Ty Ret { get; } = ret;
    /// <summary>"top" for top-level local functions, otherwise the class name.</summary>
    public string Container { get; } = container;
    public int LoopDepth { get; set; }
    public int SwitchDepth { get; set; }
    public bool InStaticLocal { get; set; }
    public string? Acc { get; set; }
}

/// <summary>
/// Type-directed, scope- and definite-assignment-aware generator for small Vibranium programs.
/// Productions live in Productions.cs; every production is general (random selectors, labels,
/// bodies, placements) and tagged through the AST (Features.cs).
/// </summary>
internal sealed partial class Gen
{
    private readonly Rng _r;
    private int _names;
    private Scope _scope;
    private FnState _fn = new(Ty.Int, "top");
    private readonly List<FuncSig> _callable = [];
    private readonly List<Stmt> _topHelpers = [];
    private readonly Dictionary<string, List<Node>> _classMembers = [];
    private readonly Dictionary<string, Scope> _classScopes = [];
    private readonly List<Node> _types = [];
    private readonly HashSet<string> _typeNames = [];
    private readonly Scope _topScope = new(null);
    private string _container = "top";
    private int _budget;

    public Gen(ulong seed)
    {
        _r = new Rng(seed);
        _scope = _topScope;
    }

    public Rng R => _r;

    private string Fresh(string prefix) => prefix + (++_names).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ───────────── scopes ─────────────

    private Scope Push(bool staticBoundary = false, bool isLoop = false)
    {
        _scope = new Scope(_scope, staticBoundary, isLoop);
        return _scope;
    }

    private void Pop() => _scope = _scope.Parent ?? _topScope;

    private Var Declare(string name, Ty type, VK kind, bool assigned = true)
    {
        var v = new Var(name, type, kind) { Assigned = assigned };
        _scope.Vars.Add(v);
        return v;
    }

    /// <summary>All variables readable from the current point, innermost first.</summary>
    private IEnumerable<Var> Visible()
    {
        bool crossedStatic = false;
        var seen = new HashSet<string>();
        for (var s = _scope; s is not null; s = s.Parent)
        {
            foreach (var v in Enumerable.Reverse(s.Vars))
            {
                if (!seen.Add(v.Name))
                {
                    continue;
                }
                if (crossedStatic && !v.Static)
                {
                    continue;
                }
                yield return v;
            }
            if (s.StaticBoundary)
            {
                crossedStatic = true;
            }
        }
    }

    private Var? Lookup(string name) => Visible().FirstOrDefault(v => v.Name == name);

    private List<Var> Readable(Func<Ty, bool> pred) =>
        [.. Visible().Where(v => v.Assigned && pred(v.Type) && v.Type.Kind is not TK.Resource)];

    private List<Var> ReadableV(Func<Var, bool> pred) =>
        [.. Visible().Where(v => v.Assigned && pred(v) && v.Type.Kind is not TK.Resource)];

    private bool InLoop => _fn.LoopDepth > 0;

    /// <summary>Functions callable from the current container (top-level local functions are invisible inside types).</summary>
    private IEnumerable<FuncSig> Callable() => _callable.Where(f => f.Container == _container || f.Container != "top");

    private string Access(FuncSig f) => f.Container == _container ? f.Name : f.Container + "." + f.Name;

    // ───────────── literals ─────────────

    private static readonly string[] Inv = [];

    private Lit IntLit(Ty t, int lo = -3, int hi = 9)
    {
        int v = _r.Range(lo, hi);
        return t.Kind switch
        {
            TK.Int => new Lit(v.ToString(System.Globalization.CultureInfo.InvariantCulture), t),
            TK.Long => new Lit(v + "L", t),
            TK.UInt => new Lit(Math.Abs(v) + "u", t),
            TK.ULong => new Lit(Math.Abs(v) + "UL", t),
            // `(nint)-2` is not a cast in C# (CS0119: nint is a contextual keyword); keep it as a rare near-miss
            TK.Nint => new Lit(v < 0 && !_r.Chance(0.15) ? "(nint)(" + v + ")" : "(nint)" + v, t),
            TK.UShort => new Lit("(ushort)" + Math.Abs(v), t),
            TK.Short => new Lit("(short)" + v, t),
            TK.Byte => new Lit("(byte)" + Math.Abs(v), t),
            TK.SByte => new Lit("(sbyte)" + v, t),
            TK.Char => new Lit("'" + (char)('a' + Math.Abs(v) % 6) + "'", t),
            TK.Float => new Lit(v + ".5f", t),
            TK.Double => new Lit(v + ".25", t),
            TK.Bool => new Lit(_r.Chance(0.5) ? "true" : "false", t),
            _ => throw new InvalidOperationException("no literal for " + t),
        };
    }

    private static bool IsConstant(Expr e) => e switch
    {
        Lit => true,
        NameExpr => false,
        CallExpr => false,
        RawExpr => false,
        MemberExpr m => m.E is NameExpr n && n.Type is null,
        _ => e.Kids.All(k => k is not Expr x || IsConstant(x)),
    };

    // ───────────── expressions ─────────────

    private static readonly Ty[] IntLike = [Ty.Int, Ty.Long, Ty.UInt, Ty.UShort, Ty.Byte, Ty.Char, Ty.Short, Ty.SByte, Ty.ULong];

    public Expr Expr(Ty t, int depth = 0)
    {
        _budget++;
        bool leaf = depth >= 3 || _budget > 400;
        var vars = Readable(v => v == t);
        if (t.Kind is TK.Enum)
        {
            var en = _types.OfType<EnumDecl>().First(e => e.Name == t.Name);
            if (vars.Count > 0 && _r.Chance(0.5))
            {
                return VarRef(_r.Pick(vars));
            }
            if (!leaf && _r.Chance(0.2))
            {
                return new CastExpr(t, new ParenExpr(Expr(Ty.Int, depth + 1)).Typed<ParenExpr>(Ty.Int)).Typed<CastExpr>(t);
            }
            var m = _r.Pick(en.Members);
            return new MemberExpr(new NameExpr(en.Name), m.Name).Typed<MemberExpr>(t);
        }
        if (t.Kind is TK.Option or TK.Result)
        {
            return UnionExpr(t, depth, vars);
        }
        if (t.Kind is TK.String)
        {
            return new Lit("\"" + _r.Pick(["ok", "x", "done", "v"]) + "\"", t);
        }
        if (leaf || _r.Chance(0.3))
        {
            if (vars.Count > 0 && _r.Chance(0.75))
            {
                return VarRef(_r.Pick(vars));
            }
            return IntLit(t);
        }
        if (t.Kind is TK.Bool)
        {
            return BoolExpr(depth);
        }
        int choice = _r.Int(100);
        if (choice < 25 && vars.Count > 0)
        {
            return VarRef(_r.Pick(vars));
        }
        if (choice < 55)
        {
            return Arith(t, depth);
        }
        if (choice < 63)
        {
            var cond = Cond(BoolExpr(depth + 1));
            return new CondExpr(cond, Expr(t, depth + 1), Expr(t, depth + 1), false).Typed<CondExpr>(t);
        }
        if (choice < 73)
        {
            var calls = Callable().Where(f => f.Ret == t && !f.Params.Any(p => p.Mod is "out" or "ref") && f.Params.All(p => ArgGenerable(p.Type))).ToList();
            if (calls.Count > 0)
            {
                return Call(_r.Pick(calls), depth + 1);
            }
        }
        if (choice < 80 && t.Kind is TK.Int)
        {
            return OtherSourceInt(depth);
        }
        if (choice < 88)
        {
            // cast from another numeric
            Ty[] fromSet = t.IsFloating ? [Ty.Int, Ty.Long] : t.Kind is TK.Int ? [Ty.Long, Ty.Double, Ty.Float, Ty.UShort, Ty.Char, Ty.Byte] : [Ty.Int, Ty.Long];
            var from = _r.Pick(fromSet);
            var inner = Expr(from, depth + 1);
            return new CastExpr(t, Paren(inner)).Typed<CastExpr>(t);
        }
        if (vars.Count > 0)
        {
            return VarRef(_r.Pick(vars));
        }
        return IntLit(t);
    }

    private static Expr Paren(Expr e) => e is Lit or NameExpr or CallExpr or MemberExpr or ParenExpr ? e : new ParenExpr(e).Typed<ParenExpr>(e.Type);

    private Expr VarRef(Var v)
    {
        var e = new NameExpr(v.Access) { Type = v.Type, IsConst = v.Kind is VK.Const or VK.EnumMember };
        return e;
    }

    // ───────────── v0.2 cuts C19 (constant conditions) ─────────────

    private static readonly System.Text.RegularExpressions.Regex RawConstRx = new(@"^\(?\s*(true|false|-?\d+(\.\d+)?[uUlLfFdDmM]*)\s*\)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether <paramref name="e"/> is a C# constant expression (literals, const names, enum members and operators over
    /// them). Used to keep conditions, guards and selectors non-constant (cut C19); conservative for calls and patterns.
    /// </summary>
    internal static bool IsCsConstant(Expr e) => e switch
    {
        Lit => true,
        NameExpr n => n.IsConst,
        RawExpr r => RawConstRx.IsMatch(r.Text.Trim()),
        CallExpr or IsPatExpr or SwitchExpr or CollExpr or OutDeclExpr => false,
        MemberExpr m => m.E is NameExpr n && n.Type is null,
        _ => e.Kids.Any(k => k is Expr) && e.Kids.All(k => k is not Expr x || IsCsConstant(x)),
    };

    /// <summary>A condition, guard or loop test: <paramref name="e"/> unless it is a C# constant (cut C19).</summary>
    private Expr Cond(Expr e)
    {
        if (!IsCsConstant(e))
        {
            return e;
        }
        var ints = ReadableV(v => v.Type == Ty.Int && v.Kind is not (VK.Const or VK.EnumMember));
        if (ints.Count > 0)
        {
            return new BinExpr(_r.Pick(["<", ">", "!="]), VarRef(_r.Pick(ints)), IntLit(Ty.Int, -1, 5)).Typed<BinExpr>(Ty.Bool);
        }
        var bools = ReadableV(v => v.Type == Ty.Bool && v.Kind is not (VK.Const or VK.EnumMember));
        if (bools.Count > 0)
        {
            var b = VarRef(_r.Pick(bools));
            return _r.Chance(0.3) ? new UnExpr("!", b).Typed<UnExpr>(Ty.Bool) : b;
        }
        var nums = ReadableV(v => v.Type.IsIntegral && !v.Type.PromotesToInt && v.Kind is not (VK.Const or VK.EnumMember));
        if (nums.Count > 0)
        {
            var v = _r.Pick(nums);
            return new BinExpr("!=", VarRef(v), IntLit(v.Type, 0, 0)).Typed<BinExpr>(Ty.Bool);
        }
        return e;
    }

    /// <summary>A switch selector of type <paramref name="t"/>: <paramref name="e"/> unless it is a C# constant (cut C19).</summary>
    private Expr Selector(Ty t, Expr e)
    {
        if (!IsCsConstant(e))
        {
            return e;
        }
        var same = ReadableV(v => v.Type == t && v.Kind is not (VK.Const or VK.EnumMember));
        if (same.Count > 0)
        {
            return VarRef(_r.Pick(same));
        }
        var ints = ReadableV(v => v.Type == Ty.Int && v.Kind is not (VK.Const or VK.EnumMember));
        if (ints.Count > 0 && (t.IsNumeric || t.Kind is TK.Enum))
        {
            var src = VarRef(_r.Pick(ints));
            return t == Ty.Int ? src : new CastExpr(t, src).Typed<CastExpr>(t);
        }
        return e;
    }

    /// <summary>int-valued expressions from other features: spans, vecs, struct fields, options, enums.</summary>
    private Expr OtherSourceInt(int depth)
    {
        var opts = new List<Func<Expr?>>
        {
            () => Readable(t => t.Kind is TK.ROSpan or TK.Span or TK.Vec).Where(v => v.Type.Arg == Ty.Int).Select(v => (Expr)new MemberExpr(VarRef(v), v.Type.Kind == TK.Vec ? "Count" : "Length").Typed<MemberExpr>(Ty.Int)).FirstOrDefault(),
            () => Readable(t => t.Kind == TK.Struct).Select(v => StructFieldRead(v)).FirstOrDefault(),
            () => Readable(t => t == Ty.OptInt).Select(v => (Expr)new CallExpr(VarRef(v), "UnwrapOr", [new Arg("", IntLit(Ty.Int))]).Typed<CallExpr>(Ty.Int)).FirstOrDefault(),
            () => Readable(t => t.Kind == TK.Enum).Select(v => (Expr)new CastExpr(Ty.Int, VarRef(v)).Typed<CastExpr>(Ty.Int)).FirstOrDefault(),
            () => SwitchExprProd(Ty.Int, depth + 1),
        };
        var pick = _r.Pick(opts)();
        return pick ?? IntLit(Ty.Int);
    }

    private Expr? StructFieldRead(Var v)
    {
        var sd = _types.OfType<StructDecl>().FirstOrDefault(s => s.Name == v.Type.Name);
        var f = sd?.Members.OfType<FieldDecl>().FirstOrDefault(x => x.Type == Ty.Int && !x.IsConst);
        return f is null ? null : new MemberExpr(VarRef(v), f.Name).Typed<MemberExpr>(Ty.Int);
    }

    private Expr Arith(Ty t, int depth)
    {
        if (t.PromotesToInt)
        {
            // (ushort)(a op b)
            var inner = Arith(Ty.Int, depth + 1);
            return new CastExpr(t, Paren(inner)).Typed<CastExpr>(t);
        }
        string op = _r.Weighted((5, "+"), (4, "-"), (4, "*"), (2, "/"), (3, "%"), (1, "&"), (1, "|"), (1, "^"), (1, "<<"), (1, ">>"));
        if (t.IsFloating && op is "&" or "|" or "^" or "<<" or ">>")
        {
            op = "%";
        }
        Expr l = Expr(t, depth + 1);
        Expr r;
        if (op is "<<" or ">>")
        {
            r = Expr(Ty.Int, depth + 1);
        }
        else if (op is "/" or "%" && !t.IsFloating && _r.Chance(0.6))
        {
            // mostly non-zero divisors; occasionally a runtime zero reaches the division
            r = _r.Chance(0.5) ? IntLit(t, 1, 7) : Expr(t, depth + 1);
        }
        else
        {
            r = Expr(t, depth + 1);
        }
        if (IsConstant(l) && IsConstant(r))
        {
            // keep constant folding (overflow, divide by constant zero) rare
            var vs = Readable(v => v == t);
            if (vs.Count > 0)
            {
                l = VarRef(_r.Pick(vs));
            }
        }
        if (op is "/" or "%" && r is Lit { Text: "0" or "0L" or "0u" or "0UL" })
        {
            r = IntLit(t, 1, 5);
        }
        return new BinExpr(op, Paren(l), Paren(r)).Typed<BinExpr>(t);
    }

    /// <summary>A bool expression that is not a C# constant when any variable allows it (conditions: cut C19).</summary>
    private Expr BoolExpr(int depth) => Cond(BoolExprCore(depth));

    private Expr BoolExprCore(int depth)
    {
        var bools = Readable(v => v == Ty.Bool);
        int c = _r.Int(100);
        if (depth >= 3 || c < 20)
        {
            var nb = bools.Where(v => v.Kind is not (VK.Const or VK.EnumMember)).ToList();
            return nb.Count > 0 && _r.Chance(0.9) ? VarRef(_r.Pick(nb)) : NonConstBool();
        }
        if (c < 55)
        {
            var t = _r.Pick([Ty.Int, Ty.Int, Ty.Long, Ty.Double, Ty.UShort]);
            var ty = t.PromotesToInt ? Ty.Int : t;
            string op = _r.Pick(["<", ">", "<=", ">=", "==", "!="]);
            var cl = Expr(t, depth + 1);
            var cr = Expr(t, depth + 1);
            if (IsCsConstant(cl) && IsCsConstant(cr))
            {
                // a comparison of two constants is a constant condition (cut C19)
                var vs = ReadableV(v => v.Type == t && v.Kind is not (VK.Const or VK.EnumMember));
                if (vs.Count > 0)
                {
                    cl = VarRef(_r.Pick(vs));
                }
            }
            return new BinExpr(op, Paren(cl), Paren(cr)).Typed<BinExpr>(Ty.Bool);
        }
        if (c < 70)
        {
            return new BinExpr(_r.Pick(["&&", "||"]), Paren(BoolExprCore(depth + 1)), Paren(BoolExprCore(depth + 1))).Typed<BinExpr>(Ty.Bool);
        }
        if (c < 80)
        {
            return new UnExpr("!", Paren(BoolExprCore(depth + 1))).Typed<UnExpr>(Ty.Bool);
        }
        if (c < 92)
        {
            // e is <pattern> without designation (designations are handled by productions)
            // a constant operand makes the pattern always or never match (CS8793/CS8794/CS8519/CS8520, errors under App.props)
            var ints = ReadableV(v => v.Type == Ty.Int && v.Kind is not (VK.Const or VK.EnumMember));
            if (ints.Count > 0)
            {
                return new IsPatExpr(VarRef(_r.Pick(ints)), SimpleIntPattern(Ty.Int)).Typed<IsPatExpr>(Ty.Bool);
            }
        }
        var opts = Readable(v => v == Ty.OptInt);
        if (opts.Count > 0)
        {
            return new CallExpr(VarRef(_r.Pick(opts)), "IsSome", []).Typed<CallExpr>(Ty.Bool);
        }
        return bools.Count > 0 ? VarRef(_r.Pick(bools)) : NonConstBool();
    }

    /// <summary>A non-constant condition when possible (constant conditions cause CS0162 unreachable-code warnings).</summary>
    private Expr NonConstBool()
    {
        var ints = ReadableV(v => v.Type == Ty.Int && v.Kind is not (VK.Const or VK.EnumMember));
        if (ints.Count > 0)
        {
            return new BinExpr(_r.Pick(["<", ">", "!="]), VarRef(_r.Pick(ints)), IntLit(Ty.Int, -1, 5)).Typed<BinExpr>(Ty.Bool);
        }
        return Cond(IntLit(Ty.Bool));
    }

    private Pat SimpleIntPattern(Ty t)
    {
        // relational and `and` patterns are cut C20: constant, `or` and `not` patterns only
        return _r.Int(4) switch
        {
            0 => new ConstPat(IntLit(t, -2, 8)),
            1 => new BinPat("or", new ConstPat(IntLit(t, -2, 2)), new ConstPat(IntLit(t, 3, 8))),
            2 => new NotPat(new BinPat("or", new ConstPat(IntLit(t, -3, 1)), new ConstPat(IntLit(t, 2, 9)))),
            _ => new NotPat(new ConstPat(IntLit(t, 0, 5))),
        };
    }

    private Expr UnionExpr(Ty t, int depth, List<Var> vars)
    {
        if (vars.Count > 0 && _r.Chance(0.4))
        {
            return VarRef(_r.Pick(vars));
        }
        var calls = Callable().Where(f => f.Ret == t && !f.Params.Any(p => p.Mod is "out" or "ref") && f.Params.All(p => ArgGenerable(p.Type))).ToList();
        if (calls.Count > 0 && _r.Chance(0.4))
        {
            return Call(_r.Pick(calls), depth + 1);
        }
        if (t.Kind is TK.Option)
        {
            return _r.Chance(0.7)
                ? new CallExpr(null, "Some", [new Arg("", Expr(t.Arg!, depth + 1))]).Typed<CallExpr>(t)
                : new CallExpr(null, "None", []).Typed<CallExpr>(t);
        }
        return _r.Chance(0.6)
            ? new CallExpr(null, "Ok", [new Arg("", Expr(t.Arg!, depth + 1))]).Typed<CallExpr>(t)
            : new CallExpr(null, "Err", [new Arg("", Expr(t.Arg2!, depth + 1))]).Typed<CallExpr>(t);
    }

    /// <summary>Parameter types <see cref="ArgFor"/> can synthesize an argument for without a matching variable.</summary>
    private static bool ArgGenerable(Ty t) => t.Kind is not (TK.Struct or TK.Class or TK.Resource or TK.Vec or TK.Span or TK.Void);

    private Expr Call(FuncSig f, int depth)
    {
        var args = new List<Arg>();
        foreach (var (pt, mod) in f.Params)
        {
            args.Add(ArgFor(pt, mod, depth) is var a && mod is "in" or "ref readonly" ? new Arg(a.Mod, a.E) { ToReadonlyRefParam = true } : a);
        }
        return new CallExpr(null, Access(f), args) { Type = f.Ret, RefReadonlyArgs = f.Params.Any(p => p.Mod is "in" or "ref readonly") };
    }

    private Arg ArgFor(Ty pt, string mod, int depth)
    {
        if (mod is "ref" or "out" or "in" or "ref readonly")
        {
            var locals = Readable(v => v == pt).Where(v => v.Kind is VK.Local or VK.Param && v.Mutable).ToList();
            if (mod == "out")
            {
                locals = [.. Visible().Where(v => v.Type == pt && v.Kind is VK.Local && v.Mutable)];
            }
            if (locals.Count > 0)
            {
                var v = _r.Pick(locals);
                v.Assigned = true;
                string m = mod switch { "ref readonly" => _r.Pick(["ref", "in", "in"]), "in" => _r.Pick(["", "in"]), _ => mod };
                return new Arg(m, VarRef(v));
            }
            if (mod == "out")
            {
                return new Arg("out", new OutDeclExpr(null, "_"));
            }
            if (mod == "in")
            {
                return new Arg("", Expr(pt, depth + 1));
            }
            // no variable: declare nothing here; pass an rvalue (near-miss, oracle decides)
            return new Arg(mod == "ref readonly" ? "" : mod, Expr(pt, depth + 1));
        }
        if (pt.Kind is TK.ROSpan)
        {
            return new Arg("", SpanArg(pt, depth));
        }
        return new Arg("", Expr(pt, depth + 1));
    }

    private Expr SpanArg(Ty pt, int depth)
    {
        var vs = Readable(v => v == pt || (v.Kind == TK.Span && v.Arg == pt.Arg));
        if (vs.Count > 0 && _r.Chance(0.5))
        {
            return VarRef(_r.Pick(vs));
        }
        return CollLit(pt, depth, allowEmpty: true);
    }

    private CollExpr CollLit(Ty collTy, int depth, bool allowEmpty = false)
    {
        int n = _r.Range(allowEmpty ? 0 : 1, 3);
        var el = collTy.Arg!;
        var elems = new List<Expr>();
        for (int i = 0; i < n; i++)
        {
            elems.Add(_r.Chance(0.5) ? Expr(el, depth + 2) : ElemConst(el));
        }
        return new CollExpr(elems) { Type = collTy };
    }

    private Expr ElemConst(Ty el)
    {
        if (el.Kind is TK.Enum)
        {
            var en = _types.OfType<EnumDecl>().First(e => e.Name == el.Name);
            return new MemberExpr(new NameExpr(en.Name), _r.Pick(en.Members).Name).Typed<MemberExpr>(el);
        }
        return IntLit(el, 0, 9);
    }

    // ───────────── statements ─────────────

    /// <summary>Generic filler statement; returns 1-2 statements.</summary>
    private List<Stmt> Filler(int depth)
    {
        _budget++;
        int c = _r.Int(100);
        if (c < 30 || depth > 2)
        {
            return [LocalDecl(_r.Pick([Ty.Int, Ty.Int, Ty.Long, Ty.Bool, Ty.Double, Ty.UShort]))];
        }
        if (c < 50 && _fn.Acc is not null)
        {
            return [AccAdd(Expr(Ty.Int, 1))];
        }
        if (c < 62)
        {
            var printable = _r.Pick([Ty.Int, Ty.Long, Ty.Bool, Ty.UInt]);
            return [Print(Expr(printable, 1))];
        }
        if (c < 75)
        {
            var muts = Readable(v => v.IsIntegral && !v.PromotesToInt).Where(v => v.Mutable && v.Kind is VK.Local).ToList();
            if (muts.Count > 0)
            {
                var v = _r.Pick(muts);
                string op = _r.Weighted((4, "="), (3, "+="), (1, "-="), (1, "*="), (1, "%="), (1, "/="));
                var val = op is "%=" or "/=" ? IntLit(v.Type, 1, 6) : Expr(v.Type, 1);
                return [new AssignStmt(VarRef(v), op, val)];
            }
        }
        if (c < 90)
        {
            var cond = Cond(BoolExpr(1));
            Push();
            var then = Body(depth + 1, 1);
            Pop();
            Stmt? els = null;
            if (_r.Chance(0.4))
            {
                Push();
                els = Body(depth + 1, 1);
                Pop();
            }
            return [new IfStmt(cond, then, els)];
        }
        return [LocalDecl(Ty.Int)];
    }

    private Block Body(int depth, int n)
    {
        var items = new List<Stmt>();
        for (int i = 0; i < n; i++)
        {
            items.AddRange(Filler(depth));
        }
        return new Block(items);
    }

    private Stmt Print(Expr e) => new ExprStmt(new CallExpr(null, "WriteLine", [new Arg("", e)]));

    private Stmt AccAdd(Expr e) => new AssignStmt(new NameExpr(_fn.Acc!) { Type = Ty.Int }, "+=", e);

    /// <summary><c>T x = e;</c> with a non-constant initializer when possible (avoids CS0219).</summary>
    private LocalDeclStmt LocalDecl(Ty t, string? name = null)
    {
        var init = Expr(t, 1);
        if (IsConstant(init))
        {
            var vs = Readable(v => v == t);
            var ints = ReadableV(v => v.Type == Ty.Int && v.Kind is not VK.Const);
            if (vs.Count > 0)
            {
                init = t.PromotesToInt || t.Kind is TK.Bool or TK.Enum ? VarRef(_r.Pick(vs)) : new BinExpr("+", VarRef(_r.Pick(vs)), init).Typed<BinExpr>(t);
            }
            else if (ints.Count > 0 && t.IsNumeric && t.Kind is not TK.Char)
            {
                // derive from a non-constant int so an unread local is not a constant assignment (CS0219)
                Expr src = VarRef(_r.Pick(ints));
                init = t == Ty.Int ? src : t.Kind is TK.Long or TK.Double or TK.Float ? new CastExpr(t, src).Typed<CastExpr>(t) : new CastExpr(t, new ParenExpr(src).Typed<ParenExpr>(Ty.Int)).Typed<CastExpr>(t);
            }
            else if (t == Ty.Bool && ints.Count > 0)
            {
                init = NonConstBool();
            }
        }
        string n = name ?? Fresh(t.Kind switch { TK.Bool => "b", TK.Double or TK.Float => "d", _ => "x" });
        Declare(n, t, VK.Local);
        bool useVar = _r.Chance(0.25) && t.Kind is TK.Int or TK.Bool or TK.Double or TK.Long && init is not Lit;
        return new LocalDeclStmt(useVar ? null : t, n, init, "");
    }

    /// <summary>Expression consuming every function-scope local so nothing is left unread (CS0168/CS0219).</summary>
    private Expr ResultExpr(Ty ret)
    {
        if (ret != Ty.Int)
        {
            return Expr(ret, 1);
        }
        Expr e = _fn.Acc is null ? Expr(Ty.Int, 1) : new NameExpr(_fn.Acc) { Type = Ty.Int };
        return e;
    }
}
