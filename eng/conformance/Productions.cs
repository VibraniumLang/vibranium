using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>Anchor productions. Each is general: its selector types, labels, placements and
/// bodies are random; the pairwise scheduler nests one anchor inside another.</summary>
internal enum Anchor
{
    SwitchStmt, SwitchExpr, OutArg, Underscore, Const, LocalFunc, StructInit, RefParams,
    Collection, CondArg, Loop, Using, OptionResult, Panic, Arith, PatternVar, ClassInstance, Banned,

    // extended profile only (--profile extended)
    SpanSlice, OptionSlot, MutReceiver, Utf8View, StdText,

    // activation profile only (--profile activation): Round-11 call-activation model and audit roots
    CaCall, CaReceiver, CaAssign, ViewReturn, CondTemp, FloatNint, StdAppend, DeclCuts,

    // soundness profile only (--profile soundness): loan producer × invalidator × use placement, expectation-free
    SndSpan, SndSlice, SndView, SndRefLocal, SndOptRef, SndOptMut, SndRecv,

    // activation profile (release-7.1): moved views, loop-reassigned views and assignment values (appended last so the
    // numbering of the other anchors is unchanged)
    ViewMove,

    // every profile (release-7.1): switch statements and expressions over Option/Result mixing the union cases with
    // whole-union labels (`not`, `or`, `var`, guards); appended last so the numbering of the other anchors is unchanged
    UnionSwitch,
}

internal delegate List<Stmt> Inner();

internal sealed partial class Gen
{
    /// <summary>
    /// The default profile's anchors, in their original order. Release-7.1 appends <c>UnionSwitch</c> (switches over
    /// Option/Result with whole-union labels); this changed the default schedule and every profile's stream once.
    /// </summary>
    public static readonly Anchor[] DefaultAnchors =
    [
        Anchor.SwitchStmt, Anchor.SwitchExpr, Anchor.OutArg, Anchor.Underscore, Anchor.Const, Anchor.LocalFunc, Anchor.StructInit, Anchor.RefParams,
        Anchor.Collection, Anchor.CondArg, Anchor.Loop, Anchor.Using, Anchor.OptionResult, Anchor.Panic, Anchor.Arith, Anchor.PatternVar, Anchor.ClassInstance, Anchor.Banned,
        Anchor.UnionSwitch,
    ];

    /// <summary>Anchors added by the extended profile (Span slicing, Option slots, [Mut] receivers, UTF-8 views, Std calls).</summary>
    public static readonly Anchor[] ExtendedAnchors = [Anchor.SpanSlice, Anchor.OptionSlot, Anchor.MutReceiver, Anchor.Utf8View, Anchor.StdText];

    public static readonly Anchor[] AllAnchors = [.. DefaultAnchors, .. ExtendedAnchors];

    /// <summary>
    /// Anchors added by the activation profile: borrow plus move in one call or assignment (the Round-11 call-activation
    /// model), views returned through parameters, conditional temporaries, constant float to native-integer
    /// conversions, Std append to a pipe, the declaration cuts C5–C9, and (release-7.1) moved and reassigned views.
    /// </summary>
    public static readonly Anchor[] ActivationAnchors =
        [Anchor.CaCall, Anchor.CaReceiver, Anchor.CaAssign, Anchor.ViewReturn, Anchor.CondTemp, Anchor.FloatNint, Anchor.StdAppend, Anchor.DeclCuts, Anchor.ViewMove];

    /// <summary>The activation profile pairs its own anchors with the default ones (banned constructs excluded).</summary>
    public static readonly Anchor[] ActivationProfileAnchors = [.. DefaultAnchors.Where(a => a != Anchor.Banned), .. ActivationAnchors];

    /// <summary>The anchors a profile draws from: <c>default</c>, <c>extended</c> or <c>activation</c>.</summary>
    public static Anchor[] Anchors(string profile) => profile switch
    {
        "extended" => AllAnchors,
        "activation" => ActivationProfileAnchors,
        "soundness" => SoundnessProfileAnchors,
        _ => DefaultAnchors,
    };

    /// <summary>True only while the outermost anchor of the main function body is starting.</summary>
    private bool _topPending;

    private List<Stmt> Run(Anchor a, Inner inner)
    {
        _isTop = _topPending;
        _topPending = false;
        return RunAnchor(a, inner);
    }

    private bool _isTop;

    private List<Stmt> RunAnchor(Anchor a, Inner inner) => a switch
    {
        Anchor.SwitchStmt => PSwitchStmt(inner),
        Anchor.SwitchExpr => PSwitchExpr(inner),
        Anchor.OutArg => POutArg(inner),
        Anchor.Underscore => PUnderscore(inner),
        Anchor.Const => PConst(inner),
        Anchor.LocalFunc => PLocalFunc(inner),
        Anchor.StructInit => PStructInit(inner),
        Anchor.RefParams => PRefParams(inner),
        Anchor.Collection => PCollection(inner),
        Anchor.CondArg => PCondArg(inner),
        Anchor.Loop => PLoop(inner),
        Anchor.Using => PUsing(inner),
        Anchor.OptionResult => POptionResult(inner),
        Anchor.Panic => PPanic(inner),
        Anchor.Arith => PArith(inner),
        Anchor.PatternVar => PPatternVar(inner),
        Anchor.ClassInstance => PClassInstance(inner),
        Anchor.Banned => PBanned(inner),
        Anchor.SpanSlice => PSpanSlice(inner),
        Anchor.OptionSlot => POptionSlot(inner),
        Anchor.MutReceiver => PMutReceiver(inner),
        Anchor.Utf8View => PUtf8View(inner),
        Anchor.StdText => PStdText(inner),
        Anchor.CaCall => PCaCall(inner),
        Anchor.CaReceiver => PCaReceiver(inner),
        Anchor.CaAssign => PCaAssign(inner),
        Anchor.ViewReturn => PViewReturn(inner),
        Anchor.CondTemp => PCondTemp(inner),
        Anchor.FloatNint => PFloatNint(inner),
        Anchor.StdAppend => PStdAppend(inner),
        Anchor.DeclCuts => PDeclCuts(inner),
        Anchor.ViewMove => PViewMove(inner),
        Anchor.UnionSwitch => PUnionSwitch(inner),
        Anchor.SndSpan or Anchor.SndSlice or Anchor.SndView or Anchor.SndRefLocal or Anchor.SndOptRef or Anchor.SndOptMut or Anchor.SndRecv => PSnd(a, inner),
        _ => throw new InvalidOperationException(),
    };

    private List<Stmt> FillerInner() => Filler(2);

    // ───────────── helpers to add declarations ─────────────

    private EnumDecl EnsureEnum()
    {
        var e = _types.OfType<EnumDecl>().FirstOrDefault();
        if (e is null)
        {
            int baseV = _r.Range(0, 3);
            e = new EnumDecl("E", [("A", baseV), ("B", baseV + _r.Range(1, 3)), ("C", baseV + _r.Range(4, 6))]);
            _types.Add(e);
            foreach (var m in e.Members)
            {
                _topScope.Vars.Add(new Var("E." + m.Name, Ty.Named(TK.Enum, "E"), VK.EnumMember) { Static = true, Mutable = false });
            }
        }
        return e;
    }

    private Ty EnumTy => Ty.Named(TK.Enum, EnsureEnum().Name);

    /// <summary>Adds a helper function in the current container and makes it callable.</summary>
    private FuncSig AddHelper(string name, Ty ret, (Ty T, string Mod, string N)[] ps, Func<Block?> body, Func<Expr?>? exprBody = null)
    {
        var saveScope = _scope;
        var saveFn = _fn;
        _fn = new FnState(ret, _container) { InStaticLocal = _container == "top" };
        _scope = new Scope(_container == "top" ? _topScope : ClassScope(_container), staticBoundary: _container == "top");
        foreach (var p in ps)
        {
            var v = Declare(p.N, p.T, VK.Param, assigned: p.Mod != "out");
            v.Mutable = p.Mod is "" or "ref" or "out";
        }
        Block? b = null;
        Expr? eb = exprBody?.Invoke();
        if (eb is null)
        {
            b = body();
        }
        _scope = saveScope;
        _fn = saveFn;
        var decl = new FuncDecl(_container == "top" ? "static" : "public static", ret, name,
            new ParamList([.. ps.Select(p => new ParamDecl(p.T, p.N, p.Mod))]), b, eb, isLocal: _container == "top");
        if (_container == "top")
        {
            _topHelpers.Add(new LocalFuncStmt(decl));
        }
        else
        {
            ClassMembers(_container).Add(decl);
        }
        var sig = new FuncSig(name, ret, [.. ps.Select(p => (p.T, p.Mod))], _container);
        _callable.Add(sig);
        return sig;
    }

    private List<Node> ClassMembers(string cls)
    {
        if (!_classMembers.TryGetValue(cls, out var list))
        {
            _classMembers[cls] = list = [];
        }
        return list;
    }

    private Scope ClassScope(string cls)
    {
        if (!_classScopes.TryGetValue(cls, out var sc))
        {
            _classScopes[cls] = sc = new Scope(_topScope);
        }
        return sc;
    }

    // ───────────── switch statement ─────────────

    private static readonly Ty[] SwitchSelTypes = [Ty.Int, Ty.Int, Ty.Bool, Ty.UShort, Ty.Byte, Ty.Char, Ty.Long, Ty.UInt, Ty.OptInt, Ty.ResIntInt, Ty.OptInt];

    /// <summary>Selector type for switch statements/expressions: mostly integral/bool/char/unions, sometimes enum or floating.</summary>
    private Ty PickSelTy()
    {
        int r = _r.Int(100);
        return r < 8 ? EnumTy : r < 12 ? _r.Pick([Ty.Double, Ty.Float]) : _r.Pick(SwitchSelTypes);
    }

    private Expr SelectorFor(Ty t, List<Stmt> pre)
    {
        // a constant selector is cut C19: only non-constant variables
        var vs = ReadableV(v => v.Type == t && v.Kind is not (VK.Const or VK.EnumMember));
        if (vs.Count > 0 && _r.Chance(0.7))
        {
            return VarRef(_r.Pick(vs));
        }
        if (t.Kind is TK.Option or TK.Result && _r.Chance(0.3))
        {
            var fn = UnionHelper(t);
            return Call(fn, 1);
        }
        var d = LocalDecl(t);
        pre.Add(d);
        return new NameExpr(d.Name) { Type = t };
    }

    /// <summary>Case labels for a selector type; may be complete (exhaustive) or not.</summary>
    private List<(Pat? Pat, string[] Binds, Ty? BindTy)> LabelsFor(Ty t, bool wantComplete, out bool complete)
    {
        var res = new List<(Pat?, string[], Ty?)>();
        complete = false;
        switch (t.Kind)
        {
            case TK.Bool:
                if (wantComplete || _r.Chance(0.5))
                {
                    res.Add((new ConstPat(new Lit("true", Ty.Bool)), [], null));
                    res.Add((new ConstPat(new Lit("false", Ty.Bool)), [], null));
                    complete = true;
                }
                else
                {
                    res.Add((new ConstPat(new Lit(_r.Chance(0.5) ? "true" : "false", Ty.Bool)), [], null));
                }
                break;
            case TK.Option:
            {
                string v = Fresh("v");
                if (_r.Chance(0.15))
                {
                    res.Add((new TypePat("Some<int>"), [], null));
                }
                else
                {
                    res.Add((new UnionPat("Some<int>", _r.Chance(0.85) ? new VarPat(v) : new DiscardPat()), [v], Ty.Int));
                }
                if (wantComplete || _r.Chance(0.6))
                {
                    res.Add((new TypePat("NoneType"), [], null));
                    complete = true;
                }
                break;
            }
            case TK.Result:
            {
                string v = Fresh("v");
                string e = Fresh("e");
                res.Add(_r.Chance(0.15) ? (new TypePat("Ok<int>"), [], null) : (new UnionPat("Ok<int>", new VarPat(v)), [v], Ty.Int));
                if (wantComplete || _r.Chance(0.6))
                {
                    res.Add(_r.Chance(0.15) ? (new TypePat("Err<int>"), [], null) : (new UnionPat("Err<int>", new VarPat(e)), [e], Ty.Int));
                    complete = true;
                }
                break;
            }
            case TK.Enum:
            {
                var en = EnsureEnum();
                foreach (var m in en.Members.Take(_r.Range(1, en.Members.Count)))
                {
                    res.Add((new ConstPat(new MemberExpr(new NameExpr(en.Name), m.Name).Typed<MemberExpr>(t)), [], null));
                }
                break;
            }
            default:
            {
                var used = new HashSet<string>();
                int n = _r.Range(1, 3);
                if ((wantComplete || _r.Chance(0.3)) && !t.IsFloating)
                {
                    // `k` then `not k`: complete for any integral type without relational patterns (cut C20)
                    var k = IntLit(t, 0, 6);
                    res.Add((new ConstPat(k), [], null));
                    res.Add((new NotPat(new ConstPat(new Lit(k.Text, t))), [], null));
                    complete = true;
                    break;
                }
                for (int i = 0; i < n; i++)
                {
                    int kind = _r.Int(10);
                    if (kind < 5)
                    {
                        var lit = IntLit(t, t.IsUnsigned ? 0 : -2, 7);
                        if (used.Add(lit.Text))
                        {
                            res.Add((new ConstPat(lit), [], null));
                        }
                    }
                    else if (kind < 8)
                    {
                        // `a or b` (relational and `and` patterns are cut C20)
                        var a = IntLit(t, t.IsUnsigned ? 0 : -1, 3);
                        var b = IntLit(t, 4, 8);
                        if (used.Add(a.Text) & used.Add(b.Text))
                        {
                            res.Add((new BinPat("or", new ConstPat(a), new ConstPat(b)), [], null));
                        }
                    }
                    else if (t == Ty.Int || t == Ty.Long)
                    {
                        string n2 = Fresh("n");
                        res.Add((_r.Chance(0.5) ? new DeclPat(t, n2) : new VarPat(n2), [n2], t));
                    }
                    else
                    {
                        var c = ReadableV(v => v.Kind == VK.Const && v.Type == t).FirstOrDefault();
                        if (c is not null && used.Add(c.Access))
                        {
                            res.Add((new ConstPat(VarRef(c)), [], null));
                        }
                    }
                }
                if ((t == Ty.Int || t == Ty.Long) && _r.Chance(0.1))
                {
                    // catch-all type pattern with discard designation, as the last label
                    res.Add((new DeclPat(t, "_"), [], null));
                    complete = true;
                }
                break;
            }
        }
        return res;
    }

    private Expr? Guard(string[] binds, Ty? bindTy)
    {
        int c = _r.Int(100);
        if (c < 72)
        {
            return null;
        }
        // constant guards (`when true`, `when false`, `when 0.0 >= 0.0`) are cut C19
        if (binds.Length > 0 && bindTy is not null && _r.Chance(0.7))
        {
            return new BinExpr(_r.Pick([">", "<", "!="]), new NameExpr(binds[0]) { Type = bindTy }, IntLit(bindTy, 0, 5)).Typed<BinExpr>(Ty.Bool);
        }
        return Cond(BoolExpr(2));
    }

    private List<Stmt> PSwitchStmt(Inner inner)
    {
        var pre = new List<Stmt>();
        var selTy = PickSelTy();
        var sel = SelectorFor(selTy, pre);
        bool top = _isTop;
        _isTop = false;
        int mode = _r.Int(3); // 0 plain, 1 assign (DA), 2 return
        if (_fn.Ret != Ty.Int)
        {
            mode = mode == 2 ? 0 : mode;
        }
        // all sections return only when the switch ends the function (otherwise CS0162 warnings);
        // nested return-mode switches keep at least one section that breaks
        bool allReturn = mode == 2 && top && _r.Chance(0.8);
        string? daVar = null;
        if (mode == 1)
        {
            daVar = Fresh("r");
            pre.Add(new LocalDeclStmt(Ty.Int, daVar, null, ""));
            Declare(daVar, Ty.Int, VK.Local, assigned: false);
        }
        var labels = LabelsFor(selTy, _r.Chance(0.4), out bool complete);
        bool hasDefault = _r.Chance(complete ? 0.25 : 0.55);
        int innerAt = _r.Int(labels.Count + (hasDefault ? 1 : 0));
        var sections = new List<SwitchSection>();
        _fn.SwitchDepth++;
        Push(); // switch block scope (direct section locals live here)
        var switchBlock = _scope;
        int idx = 0;
        // int variables declared directly in earlier sections (switch-block declaration space in C#)
        var blockVars = new List<Var>();
        foreach (var (pat, binds, bindTy) in labels.Concat(hasDefault ? [((Pat?)null, Array.Empty<string>(), (Ty?)null)] : []))
        {
            _scope = new Scope(switchBlock); // case-label pattern variables are section-scoped
            foreach (var b in binds)
            {
                Declare(b, bindTy!, VK.Pattern);
            }
            var guard = pat is null ? null : Guard(binds, bindTy);
            var lab = new CaseLabel(pat, guard);
            var body = new List<Stmt>();
            if (idx == innerAt)
            {
                body.AddRange(InSection(inner));
            }
            if (_r.Chance(0.35))
            {
                body.AddRange(SectionExtra());
            }
            if (daVar is not null && _r.Chance(0.92))
            {
                body.Add(new AssignStmt(new NameExpr(daVar) { Type = Ty.Int }, "=", Expr(Ty.Int, 1)));
            }
            if (_r.Chance(0.06))
            {
                body.Add(PanicStmt());
            }
            bool terminate = _r.Chance(0.96);
            if (terminate)
            {
                bool ret = mode == 2 && (allReturn || (_r.Chance(0.5) && idx > 0));
                body.Add(ret ? new ReturnStmt(ResultLike()) : InLoop && _r.Chance(0.2) ? new ContinueStmt() : new BreakStmt());
            }
            sections.Add(new SwitchSection(new LabelList([lab]), body));
            // declarations directly in a section are cut C18: Decut wraps such a section in a block, so a
            // section's locals never reach a later section (no switch-block declaration space)
            _scope = switchBlock;
            idx++;
        }
        Pop();
        _fn.SwitchDepth--;
        if (_r.Chance(0.15) && sections.Count > 1 && blockVars.Count == 0)
        {
            // merge two label lists into one section (case a: case b:)
            var a = sections[0];
            var b = sections[1];
            if (!a.Labels.Items.Concat(b.Labels.Items).Any(l => l.Pat is UnionPat or DeclPat or VarPat))
            {
                sections[1] = new SwitchSection(new LabelList([.. a.Labels.Items, .. b.Labels.Items]), b.Stmts);
                sections.RemoveAt(0);
            }
        }
        var res = new List<Stmt>(pre) { new SwitchStmt(sel, sections) };
        if (daVar is not null)
        {
            Lookup(daVar)!.Assigned = true; // generator is optimistic; the SDK oracle decides
            res.Add(_fn.Acc is not null ? AccAdd(new NameExpr(daVar) { Type = Ty.Int }) : Print(new NameExpr(daVar) { Type = Ty.Int }));
        }
        return res;
    }

    private Expr ResultLike() => _fn.Ret == Ty.Int ? (_fn.Acc is not null && _r.Chance(0.5) ? new NameExpr(_fn.Acc) { Type = Ty.Int } : Expr(Ty.Int, 1)) : Expr(_fn.Ret, 1);

    /// <summary>Statements placed directly in a switch section that exercise switch-block scoping.</summary>
    private List<Stmt> SectionExtra()
    {
        int c = _r.Int(100);
        if (c < 30)
        {
            return [LocalDecl(Ty.Int)];
        }
        if (c < 55)
        {
            // out var directly in a section statement
            string n = Fresh("o");
            var call = new CallExpr(null, "int.TryParse", [new Arg("", new Lit("\"" + _r.Range(1, 9) + "\"", Ty.String)), new Arg("out", new OutDeclExpr(_r.Chance(0.5) ? null : Ty.Int, n))]) { Type = Ty.Bool };
            Declare(n, Ty.Int, VK.Out);
            var list = new List<Stmt>();
            if (_r.Chance(0.5))
            {
                list.Add(new ExprStmt(call));
            }
            else
            {
                string b = Fresh("ok");
                Declare(b, Ty.Bool, VK.Local);
                list.Add(new LocalDeclStmt(Ty.Bool, b, call, ""));
                if (_fn.Acc is not null)
                {
                    list.Add(new AssignStmt(new NameExpr(_fn.Acc) { Type = Ty.Int }, "+=", new CondExpr(new NameExpr(b) { Type = Ty.Bool }, new NameExpr(n) { Type = Ty.Int }, new Lit("0", Ty.Int), false).Typed<CondExpr>(Ty.Int)));
                    return list;
                }
            }
            if (_fn.Acc is not null)
            {
                list.Add(AccAdd(new NameExpr(n) { Type = Ty.Int }));
            }
            return list;
        }
        if (c < 72)
        {
            // is-pattern variable declared in a section statement
            var ints = ReadableV(v => v.Type == Ty.Int);
            if (ints.Count > 0)
            {
                string q = Fresh("q");
                string b = Fresh("b");
                var isx = new IsPatExpr(VarRef(_r.Pick(ints)), _r.Chance(0.5) ? new DeclPat(Ty.Int, q) : new VarPat(q)).Typed<IsPatExpr>(Ty.Bool);
                var cond = new BinExpr("&&", isx, new BinExpr(">", new NameExpr(q) { Type = Ty.Int }, IntLit(Ty.Int, 0, 4)).Typed<BinExpr>(Ty.Bool)).Typed<BinExpr>(Ty.Bool);
                Declare(q, Ty.Int, VK.Pattern, assigned: false);
                Declare(b, Ty.Bool, VK.Local);
                return [new LocalDeclStmt(Ty.Bool, b, cond, ""), Print(new NameExpr(b) { Type = Ty.Bool })];
            }
            return [];
        }
        if (c < 86)
        {
            // static local function inside a section: directly (cut form) or in a braced block
            bool direct = _r.Chance(0.5);
            var (decl, call) = MakeLocalFunc(callBeforeDecl: _r.Chance(0.5));
            if (direct)
            {
                return [decl, AccOrPrint(call)];
            }
            return [new Block([decl, AccOrPrint(call)])];
        }
        // nested block with its own local
        Push();
        var d = LocalDecl(Ty.Int);
        var use = AccOrPrint(new NameExpr(d.Name) { Type = Ty.Int });
        Pop();
        return [new Block([d, use])];
    }

    private Stmt AccOrPrint(Expr e) => _fn.Acc is not null && e.Type == Ty.Int ? AccAdd(e) : Print(e);

    private List<Stmt> InSection(Inner inner)
    {
        var body = inner();
        // a using declaration directly in a section is CS8647; wrap usings in a block most of the time
        if (body.Any(s => s is LocalDeclStmt { Mods: "using" }) && _r.Chance(0.85))
        {
            return [new Block(body)];
        }
        return body;
    }

    // ───────────── switch expression ─────────────

    private Expr? SwitchExprProd(Ty resultTy, int depth)
    {
        if (depth > 2)
        {
            return null;
        }
        var selTy = PickSelTy();
        var vs = ReadableV(v => v.Type == selTy && v.Kind is not (VK.Const or VK.EnumMember));
        Expr sel;
        if (vs.Count > 0)
        {
            sel = VarRef(_r.Pick(vs));
        }
        else if (selTy.Kind is TK.Option or TK.Result)
        {
            sel = Call(UnionHelper(selTy), depth + 1);
        }
        else if (selTy.Kind is TK.Enum || selTy.IsFloating)
        {
            sel = Expr(selTy, depth + 1);
        }
        else
        {
            var ps = ReadableV(v => v.Type == Ty.Int);
            if (ps.Count == 0)
            {
                return null;
            }
            selTy = Ty.Int;
            sel = VarRef(_r.Pick(ps));
        }
        sel = Selector(selTy, sel);
        // the printer does not parenthesize the selector: C# binds `switch` tighter than binary and conditional
        // operators, so the parsed selector is the rightmost operand; keep that one non-constant too (cut C19)
        static Expr Parsed(Expr e) => e switch { BinExpr b => Parsed(b.R), CondExpr c => Parsed(c.B), _ => e };
        if (!ReferenceEquals(Parsed(sel), sel) && IsCsConstant(Parsed(sel)))
        {
            sel = new ParenExpr(sel).Typed<ParenExpr>(sel.Type);
        }
        var labels = LabelsFor(selTy, _r.Chance(0.5), out bool complete);
        var arms = new List<SwitchArm>();
        foreach (var (pat, binds, bindTy) in labels)
        {
            Push();
            foreach (var b in binds)
            {
                Declare(b, bindTy!, VK.Pattern);
            }
            var guard = Guard(binds, bindTy);
            // a guarded arm does not count for exhaustiveness (constant `when true` guards are cut C19)
            complete &= guard is null;
            arms.Add(new SwitchArm(pat!, guard, Expr(resultTy, depth + 1)));
            Pop();
        }
        if (!complete || _r.Chance(0.3) ? _r.Chance(0.85) : false)
        {
            arms.Add(new SwitchArm(new DiscardPat(), null, Expr(resultTy, depth + 1)));
        }
        return new SwitchExpr(sel, arms) { Type = resultTy };
    }

    private List<Stmt> PSwitchExpr(Inner inner)
    {
        var res = inner();
        var e = SwitchExprProd(Ty.Int, 0) ?? Expr(Ty.Int, 1);
        string n = Fresh("s");
        Declare(n, Ty.Int, VK.Local);
        res.Add(new LocalDeclStmt(_r.Chance(0.5) ? null : Ty.Int, n, e, ""));
        res.Add(AccOrPrint(new NameExpr(n) { Type = Ty.Int }));
        return res;
    }

    private FuncSig UnionHelper(Ty t)
    {
        var existing = Callable().FirstOrDefault(f => f.Ret == t && f.Params.Length == 1 && f.Params[0].Type == Ty.Int && f.Params[0].Mod == "");
        if (existing is not null && _r.Chance(0.7))
        {
            return existing;
        }
        string name = Fresh(t.Kind == TK.Option ? "Find" : "Try");
        return AddHelper(name, t, [(Ty.Int, "", "k")], () => null, () =>
        {
            var k = new NameExpr("k") { Type = Ty.Int };
            var cond = new BinExpr(_r.Pick([">", "<", "!="]), k, IntLit(Ty.Int, 0, 4)).Typed<BinExpr>(Ty.Bool);
            Expr a = t.Kind == TK.Option ? new CallExpr(null, "Some", [new Arg("", new BinExpr("*", k, IntLit(Ty.Int, 2, 4)).Typed<BinExpr>(Ty.Int))]) : new CallExpr(null, "Ok", [new Arg("", k)]);
            Expr b = t.Kind == TK.Option ? new CallExpr(null, "None", []) : new CallExpr(null, "Err", [new Arg("", new BinExpr("-", IntLit(Ty.Int, 0, 3), k).Typed<BinExpr>(Ty.Int))]);
            if (_r.Chance(0.5))
            {
                // explicit casts keep the conditional target-typing simple
                a = new CastExpr(t, a).Typed<CastExpr>(t);
            }
            return new CondExpr(cond, a, b, false) { Type = t };
        });
    }

    // ───────────── out arguments ─────────────

    private List<Stmt> POutArg(Inner inner)
    {
        var res = inner();
        // callee: BCL TryParse alias or user helper
        bool bcl = _r.Chance(0.55);
        FuncSig? helper = null;
        if (!bcl)
        {
            helper = Callable().FirstOrDefault(f => f.Ret == Ty.Bool && f.Params.Length == 2 && f.Params[0].Type == Ty.Int && f.Params[1].Mod == "out" && f.Params[1].Type == Ty.Int) ?? AddHelper(Fresh("TryGet"), Ty.Bool, [(Ty.Int, "", "k"), (Ty.Int, "out", "v")], () =>
            {
                return new Block([new AssignStmt(new NameExpr("v") { Type = Ty.Int }, "=", new BinExpr("*", new NameExpr("k") { Type = Ty.Int }, IntLit(Ty.Int, 2, 3)).Typed<BinExpr>(Ty.Int)), new ReturnStmt(new BinExpr(">", new NameExpr("k") { Type = Ty.Int }, new Lit("0", Ty.Int)).Typed<BinExpr>(Ty.Bool))]);
            });
        }
        // target form
        string form = _r.Weighted((20, "var"), (14, "typed"), (18, "underscore"), (8, "var_"), (8, "typed_"), (14, "existing"), (8, "field"), (6, "structfield"));
        Expr target;
        string? newVar = null;
        var under = Lookup("_");
        switch (form)
        {
            case "var":
            case "typed":
                newVar = Fresh("o");
                target = new OutDeclExpr(form == "var" ? null : Ty.Int, newVar);
                break;
            case "var_":
                target = new OutDeclExpr(null, "_");
                break;
            case "typed_":
                target = new OutDeclExpr(Ty.Int, "_");
                break;
            case "existing":
            {
                var locals = Visible().Where(v => v.Type == Ty.Int && v.Kind is VK.Local or VK.Param && v.Mutable).ToList();
                if (locals.Count == 0)
                {
                    var d = LocalDecl(Ty.Int);
                    res.Add(d);
                    locals = [Lookup(d.Name)!];
                }
                var v = _r.Pick(locals);
                target = VarRef(v);
                v.Assigned = true;
                break;
            }
            case "field":
            case "structfield":
            {
                var sv = Readable(t => t.Kind == TK.Struct).FirstOrDefault();
                if (sv is not null && StructFieldRead(sv) is MemberExpr me)
                {
                    target = me;
                    break;
                }
                if (_r.Chance(0.8))
                {
                    // overwriting a field of a fully initialized plain struct stays valid
                    var sty = Ty.Named(TK.Struct, EnsureStructPlain().Name);
                    string sn = Fresh("s");
                    Declare(sn, sty, VK.Local);
                    res.Add(new LocalDeclStmt(sty, sn, StructValue(EnsureStructPlain(), sty), ""));
                    target = new MemberExpr(new NameExpr(sn) { Type = sty }, _r.Pick(["A", "B"])).Typed<MemberExpr>(Ty.Int);
                    break;
                }
                target = new NameExpr("_");
                break;
            }
            default:
                target = new NameExpr("_");
                break;
        }
        var outArg = new Arg("out", target);
        CallExpr call = bcl
            ? new CallExpr(null, _r.Chance(0.85) ? "int.TryParse" : "long.TryParse", [new Arg("", new Lit("\"" + _r.Pick(["42", "7", "-3", "x", " 5 "]) + "\"", Ty.String)), outArg]) { Type = Ty.Bool }
            : new CallExpr(null, Access(helper!), [new Arg("", Expr(Ty.Int, 1)), outArg]) { Type = Ty.Bool };
        if (call.Callee == "long.TryParse")
        {
            // long out target
            outArg = new Arg("out", target is OutDeclExpr od ? new OutDeclExpr(od.DeclType is null ? null : Ty.Long, od.Name) : new NameExpr("_"));
            call = new CallExpr(null, "long.TryParse", [call.Args[0], outArg]) { Type = Ty.Bool };
            if (newVar is not null)
            {
                Declare(newVar, Ty.Long, VK.Out);
            }
        }
        else if (newVar is not null)
        {
            Declare(newVar, Ty.Int, VK.Out);
        }
        int ctxKind = _r.Int(4);
        switch (ctxKind)
        {
            case 0:
                res.Add(new ExprStmt(call));
                break;
            case 1:
                res.Add(new AssignStmt(new NameExpr("_"), "=", call));
                break;
            case 2:
            {
                Push();
                var then = new Block(Filler(2));
                Pop();
                res.Add(new IfStmt(call, then, null));
                break;
            }
            default:
            {
                string b = Fresh("ok");
                Declare(b, Ty.Bool, VK.Local);
                res.Add(new LocalDeclStmt(_r.Chance(0.5) ? null : Ty.Bool, b, call, ""));
                res.Add(Print(new NameExpr(b) { Type = Ty.Bool }));
                break;
            }
        }
        // observe the results
        if (newVar is not null)
        {
            var nv = Lookup(newVar)!;
            res.Add(Print(VarRef(nv)));
        }
        if (under is not null && under.Assigned && _r.Chance(0.8))
        {
            res.Add(Print(VarRef(under)));
        }
        else if (form == "existing" && target is NameExpr ne)
        {
            res.Add(Print(new NameExpr(ne.Name) { Type = Ty.Int }));
        }
        return res;
    }

    // ───────────── discards `_` (declaring a symbol named `_` is cut C11) ─────────────

    private List<Stmt> PUnderscore(Inner inner)
    {
        var res = new List<Stmt>(inner());
        res.AddRange(DiscardUses());
        return res;
    }

    /// <summary>The discard forms that stay valid under C11: `_ = e`, `out _`, `out var _`, `out int _`.</summary>
    private List<Stmt> DiscardUses()
    {
        var res = new List<Stmt>();
        int n = _r.Range(1, 3);
        for (int i = 0; i < n; i++)
        {
            string digits = "\"" + _r.Range(10, 99) + "\"";
            switch (_r.Int(4))
            {
                case 0:
                {
                    var calls = Callable().Where(f => f.Ret == Ty.Int && !f.Params.Any(p => p.Mod is "out" or "ref") && f.Params.All(p => ArgGenerable(p.Type))).ToList();
                    res.Add(new AssignStmt(new NameExpr("_"), "=", calls.Count > 0 && _r.Chance(0.6) ? Call(_r.Pick(calls), 1) : Expr(Ty.Int, 1)));
                    break;
                }
                case 1:
                    res.Add(new ExprStmt(new CallExpr(null, "int.TryParse", [new Arg("", new Lit(digits, Ty.String)), new Arg("out", new NameExpr("_"))]) { Type = Ty.Bool }));
                    break;
                case 2:
                {
                    string ok = Fresh("ok");
                    Declare(ok, Ty.Bool, VK.Local);
                    res.Add(new LocalDeclStmt(Ty.Bool, ok, new CallExpr(null, "int.TryParse", [new Arg("", new Lit(digits, Ty.String)), new Arg("out", new OutDeclExpr(_r.Chance(0.5) ? null : Ty.Int, "_"))]) { Type = Ty.Bool }, ""));
                    res.Add(Print(new NameExpr(ok) { Type = Ty.Bool }));
                    break;
                }
                default:
                    res.Add(new AssignStmt(new NameExpr("_"), "=", new CallExpr(null, "int.TryParse", [new Arg("", new Lit(digits, Ty.String)), new Arg("out", new NameExpr("_"))]) { Type = Ty.Bool }));
                    break;
            }
        }
        return res;
    }

    // ───────────── constants ─────────────

    private List<Stmt> PConst(Inner inner)
    {
        var res = new List<Stmt>();
        var t = _r.Pick([Ty.Int, Ty.Int, Ty.Long, Ty.UShort, Ty.Bool]);
        string k = Fresh("K");
        Expr val = t == Ty.Bool ? new Lit(_r.Chance(0.5) ? "true" : "false", t) : IntLit(t, 1, 9);
        // where the const lives: local (current scope), top-level, class const, enum
        int where = _r.Int(4);
        Var cv;
        if (where == 0 && _container != "top")
        {
            var cs = ClassScope(_container);
            cv = new Var(k, t, VK.Const) { Static = true, Mutable = false };
            cs.Vars.Add(cv);
            ClassMembers(_container).Insert(0, new FieldDecl(t, k, val, "public const"));
        }
        else if (where == 1)
        {
            var en = EnsureEnum();
            cv = Visible().First(v => v.Kind == VK.EnumMember);
            t = cv.Type;
        }
        else
        {
            cv = Declare(k, t, VK.Const);
            cv.Static = true;
            cv.Mutable = false;
            res.Add(new LocalDeclStmt(t, k, val, "const"));
        }
        res.AddRange(inner());
        // use the const in a static local function (by simple name), a case label, an in/ref readonly arg, or a shadowing block
        int use = _r.Int(5);
        switch (use)
        {
            case 0:
            case 1:
            {
                var (decl, call) = MakeLocalFunc(callBeforeDecl: _r.Chance(0.5), mustUse: cv);
                res.Add(decl);
                res.Add(AccOrPrint(call));
                break;
            }
            case 2:
            {
                if (cv.Type.IsIntegral)
                {
                    var sel = ReadableV(v => v.Type == cv.Type && v.Kind != VK.Const).FirstOrDefault();
                    if (sel is not null)
                    {
                        var sw = new SwitchStmt(VarRef(sel), [
                            new SwitchSection(new LabelList([new CaseLabel(new ConstPat(VarRef(cv)), null)]), [Print(new Lit("1", Ty.Int)), new BreakStmt()]),
                            new SwitchSection(new LabelList([new CaseLabel(null, null)]), [Print(new Lit("2", Ty.Int)), new BreakStmt()]),
                        ]);
                        res.Add(sw);
                        break;
                    }
                }
                res.Add(Print(cv.Type.IsPrintable ? VarRef(cv) : new CastExpr(Ty.Int, VarRef(cv)).Typed<CastExpr>(Ty.Int)));
                break;
            }
            case 3:
            {
                string mod = "in"; // `ref readonly` parameters are cut C10
                var callee = AddHelper(Fresh("H"), Ty.Int, [(cv.Type, mod, "v")], () => null, () => cv.Type == Ty.Int ? new BinExpr("+", new NameExpr("v") { Type = Ty.Int }, IntLit(Ty.Int, 1, 3)).Typed<BinExpr>(Ty.Int) : cv.Type == Ty.Bool
                    ? new CondExpr(new NameExpr("v") { Type = Ty.Bool }, IntLit(Ty.Int, 1, 3), IntLit(Ty.Int, 4, 6), false).Typed<CondExpr>(Ty.Int)
                    : new CastExpr(Ty.Int, new NameExpr("v")).Typed<CastExpr>(Ty.Int));
                // a constant has no address: `in K` is CS8156, so the argument is passed by value into the `in` parameter
                string am = "";
                res.Add(AccOrPrint(new CallExpr(null, Access(callee), [new Arg(am, VarRef(cv)) { ToReadonlyRefParam = true }]) { Type = Ty.Int, RefReadonlyArgs = true }));
                break;
            }
            default:
            {
                // shadow the name in a nested block (only legal for class consts / enum members)
                Push();
                var inner2 = new List<Stmt>();
                bool classConst = _classScopes.Values.Any(sc => sc.Vars.Contains(cv));
                if (cv.Kind == VK.Const && !cv.Access.Contains('.', StringComparison.Ordinal) && (classConst || _r.Chance(0.1)))
                {
                    var d = new LocalDeclStmt(Ty.Int, cv.Name, IntLit(Ty.Int, 1, 5), "");
                    inner2.Add(d);
                    inner2.Add(AccOrPrint(new NameExpr(cv.Name) { Type = Ty.Int }));
                }
                else
                {
                    inner2.Add(Print(cv.Type.IsPrintable ? VarRef(cv) : new CastExpr(Ty.Int, VarRef(cv)).Typed<CastExpr>(Ty.Int)));
                }
                Pop();
                res.Add(new Block(inner2));
                if (_r.Chance(0.5))
                {
                    var (decl, call) = MakeLocalFunc(callBeforeDecl: true, mustUse: cv);
                    res.Add(decl);
                    res.Add(AccOrPrint(call));
                }
                break;
            }
        }
        return res;
    }

    // ───────────── static local functions ─────────────

    /// <summary>Builds a static local function (optionally recursive or using a given const) and a call to it.</summary>
    private (Stmt Decl, Expr Call) MakeLocalFunc(bool callBeforeDecl, Var? mustUse = null)
    {
        string name = Fresh("L");
        var saveScope = _scope;
        var saveFn = _fn;
        _fn = new FnState(Ty.Int, _container) { InStaticLocal = true };
        _scope = new Scope(_scope, staticBoundary: true);
        Declare("a", Ty.Int, VK.Param);
        bool recursive = _r.Chance(0.25);
        Expr body;
        var a = new NameExpr("a") { Type = Ty.Int };
        if (recursive)
        {
            body = new CondExpr(new BinExpr("<=", a, new Lit("0", Ty.Int)).Typed<BinExpr>(Ty.Bool), Expr(Ty.Int, 2),
                new BinExpr("+", a, new CallExpr(null, name, [new Arg("", new BinExpr("-", a, new Lit("1", Ty.Int)).Typed<BinExpr>(Ty.Int))]) { Type = Ty.Int }).Typed<BinExpr>(Ty.Int), false).Typed<CondExpr>(Ty.Int);
        }
        else
        {
            body = Expr(Ty.Int, 1);
        }
        if (mustUse is not null)
        {
            // a const bool alone would be a constant condition (cut C19): combine it with the parameter
            var boolUse = new BinExpr(_r.Pick(["&&", "||"]), VarRef(mustUse), new BinExpr(">", a, IntLit(Ty.Int, 0, 3)).Typed<BinExpr>(Ty.Bool)).Typed<BinExpr>(Ty.Bool);
            Expr use = mustUse.Type == Ty.Int ? VarRef(mustUse) : mustUse.Type == Ty.Bool ? new CondExpr(new ParenExpr(boolUse).Typed<ParenExpr>(Ty.Bool), new Lit("1", Ty.Int), new Lit("2", Ty.Int), false).Typed<CondExpr>(Ty.Int) : new CastExpr(Ty.Int, VarRef(mustUse)).Typed<CastExpr>(Ty.Int);
            body = new BinExpr("+", Paren(body), Paren(use)).Typed<BinExpr>(Ty.Int);
        }
        Block? blockBody = null;
        Expr? exprBody = body;
        if (_r.Chance(0.4))
        {
            var stmts = new List<Stmt>();
            stmts.AddRange(Filler(2));
            stmts.Add(new ReturnStmt(body));
            blockBody = new Block(stmts);
            exprBody = null;
        }
        _scope = saveScope;
        _fn = saveFn;
        var decl = new LocalFuncStmt(new FuncDecl("static", Ty.Int, name, new ParamList([new ParamDecl(Ty.Int, "a", "")]), blockBody, exprBody, true));
        // a recursive function recurses as deep as its argument: keep it below 1000 so native call stacks never run out
        var callArg = Expr(Ty.Int, 2);
        if (recursive)
        {
            callArg = new BinExpr("%", Paren(callArg), new Lit("1000", Ty.Int)).Typed<BinExpr>(Ty.Int);
        }
        var call = new CallExpr(null, name, [new Arg("", callArg)]) { Type = Ty.Int };
        _ = callBeforeDecl;
        return (decl, call);
    }

    private List<Stmt> PLocalFunc(Inner inner)
    {
        var res = new List<Stmt>();
        int shape = _r.Int(4);
        var (decl, call) = MakeLocalFunc(_r.Chance(0.5));
        switch (shape)
        {
            case 0:
                // declared after use
                res.AddRange(inner());
                res.Add(AccOrPrint(call));
                res.Add(decl);
                break;
            case 1:
            {
                // nested in a block or in a sibling local function
                Push();
                var b = new List<Stmt>(inner()) { decl, AccOrPrint(call) };
                Pop();
                res.Add(new Block(b));
                break;
            }
            case 2:
            {
                // two local functions calling each other forward
                var (decl2, call2) = MakeLocalFunc(true);
                res.Add(decl);
                res.AddRange(inner());
                res.Add(AccOrPrint(call));
                res.Add(AccOrPrint(call2));
                res.Add(decl2);
                break;
            }
            default:
                res.Add(decl);
                res.AddRange(inner());
                res.Add(AccOrPrint(call));
                break;
        }
        return res;
    }
}
