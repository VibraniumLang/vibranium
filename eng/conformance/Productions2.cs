using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

internal sealed partial class Gen
{
    // ───────────── structs ─────────────

    private StructDecl EnsureStruct(bool owned)
    {
        string name = owned ? "SO" : "S";
        var s = _types.OfType<StructDecl>().FirstOrDefault(x => x.Name == name);
        if (s is null)
        {
            if (owned)
            {
                // owned struct: a primary constructor is the only way to build a fully initialized value
                // (fieldwise init of an owned struct is a v0.2 cut, `new SO()` is VIB008, object initializers are VIB000)
                var ownedField = _r.Chance(0.5) ? new FieldDecl(Ty.VecInt, "V", new NameExpr("n"), "public") : new FieldDecl(Ty.Named(TK.Class, EnsureNode().Name), "N", new NameExpr("n"), "public");
                s = new StructDecl(name, "", null, [new FieldDecl(Ty.Int, "A", new NameExpr("a"), "public"), new FieldDecl(Ty.Int, "B", new NameExpr("b"), "public"), ownedField])
                {
                    PrimaryCtor = "int a, int b, " + ownedField.Type.Cs + " n",
                };
            }
            else
            {
                s = new StructDecl(name, "", null, [new FieldDecl(Ty.Int, "A", null, "public"), new FieldDecl(Ty.Int, "B", null, "public")]);
            }
            _types.Add(s);
        }
        return s;
    }

    private StructDecl EnsureStructPlain() => EnsureStruct(false);

    /// <summary>
    /// A fully initialized struct value: <c>new S()</c> for the plain struct, the primary constructor for owned
    /// structs; rarely a banned-by-design form (object initializer VIB000, <c>new SO()</c> VIB008).
    /// </summary>
    private Expr StructValue(StructDecl sd, Ty sty)
    {
        if (_r.Chance(0.06))
        {
            return new RawExpr("new " + sd.Name + " { A = " + Printer.Expr(IntLit(Ty.Int, 1, 9)) + ", B = 2 }", ["banned.object-initializer"]) { Type = sty };
        }
        if (sd.PrimaryCtor is null)
        {
            return new RawExpr("new " + sd.Name + "()", ["struct.new-default"]) { Type = sty };
        }
        if (_r.Chance(0.06))
        {
            return new RawExpr("new " + sd.Name + "()", ["banned.default-nonzerosafe"]) { Type = sty };
        }
        var owned = sd.Members.OfType<FieldDecl>().First(f => f.Type.Kind is TK.Vec or TK.Class);
        return new NewExpr(sd.Name, [new Arg("", Expr(Ty.Int, 2)), new Arg("", IntLit(Ty.Int, 1, 5)), new Arg("", OwnedFieldInit(owned))]) { Type = sty };
    }

    private ClassDecl EnsureNode()
    {
        var c = _types.OfType<ClassDecl>().FirstOrDefault(x => x.Name == "Node");
        if (c is null)
        {
            c = new ClassDecl("Node", "sealed", "int v", null, [new FieldDecl(Ty.Int, "Value", new NameExpr("v"), "public")]);
            _types.Add(c);
        }
        return c;
    }

    private Expr OwnedFieldInit(FieldDecl f) => f.Type.Kind == TK.Vec
        ? (_r.Chance(0.5) ? new NewExpr("Vec<int>", []) { Type = f.Type } : new CollExpr([IntLit(Ty.Int, 1, 5)]) { Type = f.Type })
        : new NewExpr("Node", [new Arg("", Expr(Ty.Int, 2))]) { Type = f.Type };

    private List<Stmt> PStructInit(Inner inner)
    {
        var res = new List<Stmt>();
        bool owned = _r.Chance(0.35);
        var sd = EnsureStruct(owned);
        var sty = Ty.Named(TK.Struct, sd.Name);
        var fields = sd.Members.OfType<FieldDecl>().ToList();
        string s = Fresh("s");
        int shape = _r.Int(7);
        var sv = Declare(s, sty, VK.Local, assigned: false);
        Stmt AssignField(FieldDecl f) => new AssignStmt(new MemberExpr(new NameExpr(s) { Type = sty }, f.Name).Typed<MemberExpr>(f.Type), "=", f.Type == Ty.Int ? Expr(Ty.Int, 1) : OwnedFieldInit(f));
        switch (shape)
        {
            case 0:
            case 1:
            {
                // fieldwise: full (or partial) init, then whole-struct use
                res.Add(new LocalDeclStmt(sty, s, null, ""));
                var order = fields.ToList();
                _r.Shuffle(order);
                int n = _r.Chance(0.8) ? order.Count : _r.Range(1, order.Count - 1);
                foreach (var f in order.Take(n))
                {
                    res.Add(AssignField(f));
                }
                res.AddRange(inner());
                sv.Assigned = true;
                res.Add(StructUse(sv, sd));
                break;
            }
            case 2:
            {
                // branch-wise init
                res.Add(new LocalDeclStmt(sty, s, null, ""));
                var cond = BoolExpr(1);
                var a = new Block([.. fields.Select(AssignField)]);
                var b = new Block([.. fields.Where(_ => _r.Chance(0.9)).Select(AssignField)]);
                res.Add(new IfStmt(cond, a, b));
                res.AddRange(inner());
                sv.Assigned = true;
                res.Add(StructUse(sv, sd));
                break;
            }
            case 3:
            {
                // out parameter initialized fieldwise in the callee
                var init = Callable().FirstOrDefault(f => f.Params.Length == 1 && f.Params[0].Type == sty && f.Params[0].Mod == "out")
                    ?? AddHelper(Fresh("Init"), Ty.Void, [(sty, "out", "o")], () => new Block([.. fields.Where(_ => _r.Chance(0.92)).Select(f =>
                        (Stmt)new AssignStmt(new MemberExpr(new NameExpr("o") { Type = sty }, f.Name).Typed<MemberExpr>(f.Type), "=", f.Type == Ty.Int ? IntLit(Ty.Int, 1, 9) : OwnedFieldInit(f)))]));
                res.Add(new ExprStmt(new CallExpr(null, Access(init), [new Arg("out", _r.Chance(0.5) ? new OutDeclExpr(sty, s) : new OutDeclExpr(null, s))])));
                res.AddRange(inner());
                sv.Assigned = true;
                res.Add(StructUse(sv, sd));
                break;
            }
            case 4:
            {
                // object initializer / new S() then overwrite fields of a fully initialized struct
                Expr init = StructValue(sd, sty);
                res.Add(new LocalDeclStmt(sty, s, init, ""));
                sv.Assigned = true;
                res.AddRange(inner());
                res.Add(AssignField(fields[_r.Int(fields.Count)]));
                res.Add(StructUse(sv, sd));
                break;
            }
            case 5:
            {
                // full init by constructor-less field writes, then copy / move into another local and mutate
                res.Add(new LocalDeclStmt(sty, s, null, ""));
                foreach (var f in fields)
                {
                    res.Add(AssignField(f));
                }
                sv.Assigned = true;
                string t2 = Fresh("t");
                var tv = Declare(t2, sty, VK.Local);
                res.Add(new LocalDeclStmt(_r.Chance(0.5) ? sty : null, t2, new NameExpr(s) { Type = sty }, ""));
                res.AddRange(inner());
                res.Add(AssignField(fields[0]) is AssignStmt asg ? new AssignStmt(new MemberExpr(new NameExpr(t2) { Type = sty }, fields[0].Name).Typed<MemberExpr>(fields[0].Type), "=", asg.Value) : new BreakStmt());
                res.Add(StructUse(tv, sd));
                break;
            }
            default:
            {
                // nested struct: fieldwise init through a field path
                string outerName = owned ? "SNO" : "SN";
                var outer = _types.OfType<StructDecl>().FirstOrDefault(x => x.Name == outerName);
                if (outer is null)
                {
                    outer = new StructDecl(outerName, "", null, [new FieldDecl(sty, "In", null, "public"), new FieldDecl(Ty.Int, "C", null, "public")]);
                    _types.Add(outer);
                }
                var oty = Ty.Named(TK.Struct, outerName);
                string o = Fresh("n");
                var ov = Declare(o, oty, VK.Local, assigned: false);
                res.Add(new LocalDeclStmt(oty, o, null, ""));
                foreach (var f in fields)
                {
                    if (_r.Chance(0.93))
                    {
                        res.Add(new AssignStmt(new MemberExpr(new MemberExpr(new NameExpr(o) { Type = oty }, "In").Typed<MemberExpr>(sty), f.Name).Typed<MemberExpr>(f.Type), "=", f.Type == Ty.Int ? Expr(Ty.Int, 1) : OwnedFieldInit(f)));
                    }
                }
                res.Add(new AssignStmt(new MemberExpr(new NameExpr(o) { Type = oty }, "C").Typed<MemberExpr>(Ty.Int), "=", Expr(Ty.Int, 1)));
                res.AddRange(inner());
                ov.Assigned = true;
                res.Add(AccOrPrint(new BinExpr("+", new MemberExpr(new MemberExpr(new NameExpr(o) { Type = oty }, "In").Typed<MemberExpr>(sty), "A").Typed<MemberExpr>(Ty.Int), new MemberExpr(new NameExpr(o) { Type = oty }, "C").Typed<MemberExpr>(Ty.Int)).Typed<BinExpr>(Ty.Int)));
                // the struct local s is unused in this shape; declare it fully for later productions
                sv.Assigned = false;
                break;
            }
        }
        return res;
    }

    private Stmt StructUse(Var sv, StructDecl sd)
    {
        var sty = sv.Type;
        var me = new NameExpr(sv.Name) { Type = sty };
        switch (_r.Int(4))
        {
            case 0:
                return AccOrPrint(new BinExpr("+", new MemberExpr(me, "A").Typed<MemberExpr>(Ty.Int), new MemberExpr(new NameExpr(sv.Name) { Type = sty }, "B").Typed<MemberExpr>(Ty.Int)).Typed<BinExpr>(Ty.Int));
            case 1:
            {
                var sum = Callable().FirstOrDefault(f => f.Params.Length == 1 && f.Params[0].Type == sty && f.Params[0].Mod != "out")
                    ?? AddHelper(Fresh("Sum"), Ty.Int, [(sty, _r.Pick(["", "in"]), "x")], () => null, () =>
                        new BinExpr("+", new MemberExpr(new NameExpr("x") { Type = sty }, "A").Typed<MemberExpr>(Ty.Int), new MemberExpr(new NameExpr("x") { Type = sty }, "B").Typed<MemberExpr>(Ty.Int)).Typed<BinExpr>(Ty.Int));
                return AccOrPrint(new CallExpr(null, Access(sum), [new Arg("", me)]) { Type = Ty.Int });
            }
            case 2:
                return AccOrPrint(new MemberExpr(me, "A").Typed<MemberExpr>(Ty.Int));
            default:
            {
                var f = sd.Members.OfType<FieldDecl>().FirstOrDefault(x => x.Type.Kind == TK.Vec);
                if (f is not null)
                {
                    return AccOrPrint(new MemberExpr(new MemberExpr(me, f.Name).Typed<MemberExpr>(f.Type), "Count").Typed<MemberExpr>(Ty.Int));
                }
                return AccOrPrint(new MemberExpr(me, "B").Typed<MemberExpr>(Ty.Int));
            }
        }
    }

    // ───────────── ref / in / ref readonly / out parameters ─────────────

    private List<Stmt> PRefParams(Inner inner)
    {
        var res = inner();
        // `ref readonly` parameters are cut C10: `in` takes their share
        string mod = _r.Weighted((4, "ref"), (6, "in"), (2, "out"), (1, ""));
        var t = _r.Chance(0.8) ? Ty.Int : Ty.Named(TK.Struct, EnsureStruct(false).Name);
        string fname = Fresh("M");
        var sig = AddHelper(fname, Ty.Int, [(t, mod, "p"), (Ty.Int, "", "k")], () =>
        {
            var p = new NameExpr("p") { Type = t };
            var k = new NameExpr("k") { Type = Ty.Int };
            var stmts = new List<Stmt>();
            Expr pRead = t == Ty.Int ? p : new MemberExpr(p, "A").Typed<MemberExpr>(Ty.Int);
            if (mod is "ref" or "out")
            {
                if (t == Ty.Int)
                {
                    stmts.Add(new AssignStmt(p, mod == "out" ? "=" : "+=", k));
                }
                else
                {
                    if (mod == "out")
                    {
                        stmts.Add(new AssignStmt(new MemberExpr(new NameExpr("p") { Type = t }, "B").Typed<MemberExpr>(Ty.Int), "=", new Lit("1", Ty.Int)));
                        stmts.Add(new AssignStmt(new MemberExpr(new NameExpr("p") { Type = t }, "A").Typed<MemberExpr>(Ty.Int), "=", k));
                    }
                    else
                    {
                        stmts.Add(new AssignStmt(new MemberExpr(new NameExpr("p") { Type = t }, "A").Typed<MemberExpr>(Ty.Int), "+=", k));
                    }
                }
            }
            stmts.Add(new ReturnStmt(new BinExpr("*", Paren(pRead), IntLit(Ty.Int, 1, 3)).Typed<BinExpr>(Ty.Int)));
            return new Block(stmts);
        });
        // argument form
        string form = _r.Weighted((6, "local"), (2, "const"), (2, "literal"), (2, "rvalue"), (2, "field"), (1, "vecelem"), (1, "outdecl"), (2, "reflocal"));
        Expr arg;
        var tyLocal = Visible().Where(v => v.Type == t && v.Kind is VK.Local && v.Mutable && (v.Assigned || mod == "out")).ToList();
        switch (form)
        {
            case "const":
                if (t == Ty.Int)
                {
                    var cv = Visible().FirstOrDefault(v => v.Kind == VK.Const && v.Type == Ty.Int);
                    if (cv is null)
                    {
                        string kn = Fresh("K");
                        cv = Declare(kn, Ty.Int, VK.Const);
                        cv.Static = true;
                        cv.Mutable = false;
                        res.Add(new LocalDeclStmt(Ty.Int, kn, IntLit(Ty.Int, 1, 9), "const"));
                    }
                    arg = VarRef(cv);
                    break;
                }
                goto case "local";
            case "literal":
                arg = t == Ty.Int ? IntLit(Ty.Int, 0, 9) : StructValue(EnsureStruct(false), t);
                break;
            case "rvalue":
                arg = t == Ty.Int ? new BinExpr("+", Expr(Ty.Int, 2), IntLit(Ty.Int, 1, 3)).Typed<BinExpr>(Ty.Int) : StructValue(EnsureStruct(false), t);
                break;
            case "field":
            {
                var sv = Readable(x => x.Kind == TK.Struct && x.Name == "S").FirstOrDefault();
                if (sv is not null && t == Ty.Int)
                {
                    arg = new MemberExpr(VarRef(sv), "A").Typed<MemberExpr>(Ty.Int);
                    break;
                }
                goto case "local";
            }
            case "vecelem":
                if (t == Ty.Int)
                {
                    string vn = Fresh("v");
                    Declare(vn, Ty.VecInt, VK.Local);
                    res.Add(new LocalDeclStmt(Ty.VecInt, vn, new CollExpr([IntLit(Ty.Int, 1, 9), IntLit(Ty.Int, 1, 9)]) { Type = Ty.VecInt }, ""));
                    arg = new IndexExpr(new NameExpr(vn) { Type = Ty.VecInt }, IntLit(Ty.Int, 0, 1)).Typed<IndexExpr>(Ty.Int);
                    break;
                }
                goto case "local";
            case "reflocal":
                if (t == Ty.Int && mod is "ref" or "in" or "ref readonly")
                {
                    // ref / ref readonly local aliasing a mutable int local, passed on by reference
                    if (tyLocal.Count == 0)
                    {
                        var d0 = LocalDecl(Ty.Int);
                        res.Add(d0);
                        tyLocal = [Lookup(d0.Name)!];
                    }
                    var target = _r.Pick(tyLocal);
                    target.Assigned = true;
                    bool ro = mod is "in" or "ref readonly" && _r.Chance(0.5);
                    string rn = Fresh("r");
                    Declare(rn, Ty.Int, VK.Local).Mutable = !ro;
                    res.Add(new LocalDeclStmt(Ty.Int, rn, VarRef(target), ro ? "ref readonly" : "ref"));
                    arg = new NameExpr(rn) { Type = Ty.Int };
                    break;
                }
                goto case "local";
            case "outdecl":
                if (mod == "out")
                {
                    string on = Fresh("o");
                    Declare(on, t, VK.Out);
                    arg = new OutDeclExpr(_r.Chance(0.5) ? null : t, on);
                    break;
                }
                goto case "local";
            case "local":
            default:
            {
                if (tyLocal.Count == 0)
                {
                    if (t == Ty.Int)
                    {
                        var d = LocalDecl(Ty.Int);
                        res.Add(d);
                        tyLocal = [Lookup(d.Name)!];
                    }
                    else
                    {
                        string sn = Fresh("s");
                        var sv = Declare(sn, t, VK.Local);
                        res.Add(new LocalDeclStmt(t, sn, StructValue(EnsureStruct(false), t), ""));
                        tyLocal = [sv];
                    }
                }
                var v = _r.Pick(tyLocal);
                v.Assigned = true;
                arg = VarRef(v);
                break;
            }
        }
        // explicit modifier at the call site: usually matching, sometimes not
        string argMod = mod switch
        {
            "ref" => _r.Chance(0.9) ? "ref" : _r.Pick(["", "in"]),
            "out" => _r.Chance(0.95) ? "out" : "ref",
            "in" => _r.Pick(["", "", "in", "ref"]),
            "ref readonly" => _r.Pick(["ref", "in", "in", ""]),
            _ => _r.Chance(0.9) ? "" : "in",
        };
        var call = new CallExpr(null, Access(sig), [new Arg(argMod, arg) { ToReadonlyRefParam = mod is "in" or "ref readonly" }, new Arg("", IntLit(Ty.Int, 1, 5))]) { Type = Ty.Int, RefReadonlyArgs = mod is "in" or "ref readonly" };
        if (_r.Chance(0.25))
        {
            // conditional expression as an ordinary argument alongside
            res.Add(AccOrPrint(new BinExpr("+", call, Paren(new CondExpr(Cond(BoolExpr(2)), Expr(Ty.Int, 2), Expr(Ty.Int, 2), false).Typed<CondExpr>(Ty.Int))).Typed<BinExpr>(Ty.Int)));
        }
        else
        {
            res.Add(AccOrPrint(call));
        }
        if (arg is NameExpr n2 && t == Ty.Int && Lookup(n2.Name) is { Assigned: true, Kind: VK.Local or VK.Out })
        {
            res.Add(Print(new NameExpr(n2.Name) { Type = Ty.Int }));
        }
        else if (arg is OutDeclExpr od && t == Ty.Int)
        {
            res.Add(Print(new NameExpr(od.Name) { Type = Ty.Int }));
        }
        return res;
    }

    // ───────────── collection expressions ─────────────

    private List<Stmt> PCollection(Inner inner)
    {
        var res = inner();
        var el = _r.Weighted((6, Ty.Int), (2, Ty.Char), (1, Ty.Byte), (1, Ty.Long), (2, (Ty?)null)) ?? EnumTy;
        int shape = _r.Int(5);
        Expr ElemToInt(Expr e) => el == Ty.Int ? e : new CastExpr(Ty.Int, e).Typed<CastExpr>(Ty.Int);
        switch (shape)
        {
            case 0:
            {
                // local initialized from a collection literal; Span / ReadOnlySpan / Vec
                var kind = el == Ty.Int ? _r.Pick([TK.ROSpan, TK.Span, TK.Vec]) : TK.ROSpan;
                var ct = new Ty(kind, Arg: el);
                string n = Fresh(kind == TK.Vec ? "v" : "sp");
                Declare(n, ct, VK.Local);
                Expr init = el == Ty.Byte && _r.Chance(0.4)
                    ? new Lit("\"" + _r.Pick(["ab", "xyz", "q", "hello"]) + "\"u8", ct)
                    : CollLit(ct, 1);
                res.Add(new LocalDeclStmt(ct, n, init, ""));
                res.Add(CollUse(n, ct, ElemToInt));
                break;
            }
            case 1:
            {
                // conditional of collections as initializer
                var ct = Ty.ROSpan(el);
                string n = Fresh("sp");
                Declare(n, ct, VK.Local);
                res.Add(new LocalDeclStmt(ct, n, CondColl(ct, res), ""));
                res.Add(CollUse(n, ct, ElemToInt));
                break;
            }
            case 2:
            case 3:
            {
                // collection (maybe conditional) passed to a span parameter
                var ct = Ty.ROSpan(el);
                var first = Callable().FirstOrDefault(f => f.Params.Length == 1 && f.Params[0].Type == ct)
                    ?? AddHelper(Fresh("First"), Ty.Int, [(ct, _r.Chance(0.15) ? "scoped" : "", "q")], () => null, () =>
                    {
                        var q = new NameExpr("q") { Type = ct };
                        return new CondExpr(new BinExpr(">", new MemberExpr(q, "Length").Typed<MemberExpr>(Ty.Int), new Lit("0", Ty.Int)).Typed<BinExpr>(Ty.Bool),
                            ElemToInt(new IndexExpr(new NameExpr("q") { Type = ct }, new Lit("0", Ty.Int)).Typed<IndexExpr>(el)), new Lit("-1", Ty.Int), false) { Type = Ty.Int };
                    });
                Expr arg = shape == 2 ? CollLit(ct, 1, allowEmpty: true) : CondColl(ct, res);
                res.Add(AccOrPrint(new CallExpr(null, Access(first), [new Arg("", arg)]) { Type = Ty.Int }));
                break;
            }
            default:
            {
                // a function returning a span built from a collection literal (constants or not), maybe after Panic
                var ct = Ty.ROSpan(el);
                bool panicFirst = _r.Chance(0.4);
                var g = AddHelper(Fresh("G"), ct, [(Ty.Int, "", "k")], () =>
                {
                    var stmts = new List<Stmt>();
                    if (panicFirst)
                    {
                        // guarded, or unconditional (the return stays C#-reachable but is runtime-unreachable, §3.7)
                        stmts.Add(_r.Chance(0.5)
                            ? new IfStmt(new BinExpr("<", new NameExpr("k") { Type = Ty.Int }, new Lit("100", Ty.Int)).Typed<BinExpr>(Ty.Bool), PanicStmt(), null)
                            : PanicStmt());
                    }
                    var lit = _r.Chance(0.7) ? new CollExpr([ElemConst(el), ElemConst(el)]) { Type = ct } : CollLit(ct, 1);
                    stmts.Add(new ReturnStmt(lit));
                    return new Block(stmts);
                });
                res.Add(AccOrPrint(ElemToInt(new IndexExpr(new CallExpr(null, Access(g), [new Arg("", Expr(Ty.Int, 2))]) { Type = ct }, new Lit("0", Ty.Int)).Typed<IndexExpr>(el))));
                break;
            }
        }
        return res;
    }

    /// <summary>
    /// A conditional choosing between two collections. A collection expression as a `?:` branch is cut C16, so the
    /// collections go into locals first (added to <paramref name="pre"/>) and the conditional chooses between them.
    /// </summary>
    private Expr CondColl(Ty ct, List<Stmt> pre)
    {
        var c = Cond(BoolExpr(2));
        string an = Fresh("ca");
        string bn = Fresh("cb");
        pre.Add(new LocalDeclStmt(ct, an, CollLit(ct, 1), ""));
        pre.Add(new LocalDeclStmt(ct, bn, CollLit(ct, 1), ""));
        Declare(an, ct, VK.Local);
        Declare(bn, ct, VK.Local);
        return new CondExpr(Paren(c), new NameExpr(an) { Type = ct }, new NameExpr(bn) { Type = ct }, _r.Chance(0.3)) { Type = ct };
    }

    private Stmt CollUse(string n, Ty ct, Func<Expr, Expr> toInt)
    {
        var nv = new NameExpr(n) { Type = ct };
        switch (_r.Int(4))
        {
            case 0:
            {
                string x = Fresh("e");
                Push(isLoop: true);
                Declare(x, ct.Arg!, VK.Local);
                var body = new Block([AccOrPrint(toInt(new NameExpr(x) { Type = ct.Arg }))]);
                Pop();
                return new ForeachStmt(false, null, x, nv, body);
            }
            case 1:
                return AccOrPrint(new MemberExpr(nv, ct.Kind == TK.Vec ? "Count" : "Length").Typed<MemberExpr>(Ty.Int));
            case 2:
                if (ct.Kind is TK.Vec or TK.Span && ct.Arg == Ty.Int)
                {
                    return new AssignStmt(new IndexExpr(nv, new Lit("0", Ty.Int)).Typed<IndexExpr>(Ty.Int), _r.Pick(["=", "+="]), Expr(Ty.Int, 2));
                }
                goto default;
            default:
                return AccOrPrint(toInt(new IndexExpr(nv, new Lit(_r.Chance(0.85) ? "0" : "^1", Ty.Int)).Typed<IndexExpr>(ct.Arg)));
        }
    }

    // ───────────── conditional expressions as arguments ─────────────

    private List<Stmt> PCondArg(Inner inner)
    {
        var res = inner();
        var t = _r.Pick([Ty.Int, Ty.Int, Ty.OptInt, Ty.Long, Ty.Bool]);
        var f = Callable().FirstOrDefault(x => x.Params.Length >= 1 && x.Params[0].Type == t && x.Params[0].Mod == "" && x.Ret == Ty.Int)
            ?? AddHelper(Fresh("P"), Ty.Int, [(t, "", "c0"), (Ty.Int, "", "c1")], () => null, () =>
            {
                Expr p = t == Ty.Int ? new NameExpr("c0") { Type = Ty.Int }
                    : t == Ty.OptInt ? new CallExpr(new NameExpr("c0") { Type = t }, "UnwrapOr", [new Arg("", new Lit("-1", Ty.Int))]) { Type = Ty.Int }
                    : t == Ty.Bool ? new CondExpr(new NameExpr("c0") { Type = Ty.Bool }, new Lit("1", Ty.Int), new Lit("0", Ty.Int), false) { Type = Ty.Int }
                    : new CastExpr(Ty.Int, new NameExpr("c0") { Type = t }).Typed<CastExpr>(Ty.Int);
                return new BinExpr("+", p, new NameExpr("c1") { Type = Ty.Int }).Typed<BinExpr>(Ty.Int);
            });
        var args = new List<Arg> { new(string.Empty, new CondExpr(BoolExpr(2), Expr(t, 2), Expr(t, 2), _r.Chance(0.2)) { Type = t }) };
        for (int i = 1; i < f.Params.Length; i++)
        {
            args.Add(ArgFor(f.Params[i].Type, f.Params[i].Mod, 2));
        }
        res.Add(AccOrPrint(new CallExpr(null, Access(f), args) { Type = Ty.Int }));
        return res;
    }

    // ───────────── loops ─────────────

    private List<Stmt> PLoop(Inner inner)
    {
        var res = new List<Stmt>();
        int kind = _r.Int(4);
        _fn.LoopDepth++;
        Stmt loop;
        switch (kind)
        {
            case 0:
            {
                string i = Fresh("i");
                Push(isLoop: true);
                Declare(i, Ty.Int, VK.Local);
                var body = LoopBody(inner);
                Pop();
                loop = new ForStmt(new LocalDeclStmt(Ty.Int, i, new Lit("0", Ty.Int), ""), new BinExpr("<", new NameExpr(i) { Type = Ty.Int }, IntLit(Ty.Int, 1, 4)).Typed<BinExpr>(Ty.Bool),
                    new AssignStmt(new NameExpr(i) { Type = Ty.Int }, "+=", new Lit("1", Ty.Int)), body);
                break;
            }
            case 1:
            {
                string c = Fresh("c");
                Declare(c, Ty.Int, VK.Local);
                res.Add(new LocalDeclStmt(Ty.Int, c, new Lit("0", Ty.Int), ""));
                Push(isLoop: true);
                var body = LoopBody(inner);
                body.Items.Insert(0, new AssignStmt(new NameExpr(c) { Type = Ty.Int }, "+=", new Lit("1", Ty.Int)));
                Pop();
                var cond = new BinExpr("<", new NameExpr(c) { Type = Ty.Int }, IntLit(Ty.Int, 1, 4)).Typed<BinExpr>(Ty.Bool);
                loop = _r.Chance(0.6) ? new WhileStmt(cond, body) : new DoStmt(body, cond);
                break;
            }
            default:
            {
                string x = Fresh("e");
                var ct = _r.Pick([Ty.VecInt, Ty.ROSpan(Ty.Int), Ty.Span(Ty.Int)]);
                string cn = Fresh("v");
                Declare(cn, ct, VK.Local);
                res.Add(new LocalDeclStmt(ct, cn, CollLit(ct, 1), ""));
                bool isRef = ct.Kind is TK.Vec or TK.Span && _r.Chance(0.3);
                Push(isLoop: true);
                Declare(x, Ty.Int, VK.Local).Mutable = isRef;
                var body = LoopBody(inner);
                if (isRef)
                {
                    body.Items.Insert(0, new AssignStmt(new NameExpr(x) { Type = Ty.Int }, "+=", new Lit("1", Ty.Int)));
                }
                body.Items.Add(AccOrPrint(new NameExpr(x) { Type = Ty.Int }));
                Pop();
                Expr src = new NameExpr(cn) { Type = ct };
                if (!isRef && _r.Chance(0.05))
                {
                    // near-miss: a bare collection literal has no natural type in C#
                    src = CollLit(ct, 1);
                }
                loop = new ForeachStmt(isRef, null, x, src, body);
                break;
            }
        }
        _fn.LoopDepth--;
        res.Add(loop);
        return res;
    }

    private Block LoopBody(Inner inner)
    {
        var items = new List<Stmt>(inner());
        if (_r.Chance(0.35))
        {
            items.Add(new IfStmt(BoolExpr(2), _r.Chance(0.5) ? new BreakStmt() : new ContinueStmt(), null));
        }
        if (_r.Chance(0.5))
        {
            items.AddRange(Filler(2));
        }
        return new Block(items);
    }

    // ───────────── using / linear resources ─────────────

    private ClassDecl EnsureResource()
    {
        var c = _types.OfType<ClassDecl>().FirstOrDefault(x => x.Name == "Res");
        if (c is null)
        {
            c = new ClassDecl("Res", "sealed", "int id", "IDisposable", [
                new FieldDecl(Ty.Int, "Id", new NameExpr("id"), "public"),
                new FuncDecl("public", Ty.Void, "Dispose", new ParamList([]), null, new CallExpr(null, "WriteLine", [new Arg("", new BinExpr("+", new NameExpr("Id"), new Lit("100", Ty.Int)).Typed<BinExpr>(Ty.Int))]), false),
            ]);
            _types.Add(c);
        }
        return c;
    }

    private List<Stmt> PUsing(Inner inner)
    {
        EnsureResource();
        var rty = Ty.Named(TK.Resource, "Res");
        var res = new List<Stmt>();
        string r = Fresh("r");
        int shape = _r.Int(5);
        Expr mk() => new NewExpr("Res", [new Arg("", IntLit(Ty.Int, 1, 9))]) { Type = rty };
        switch (shape)
        {
            case 0:
            case 1:
            {
                Push();
                var items = new List<Stmt> { new LocalDeclStmt(_r.Chance(0.5) ? null : rty, r, mk(), "using") };
                Declare(r, rty, VK.Local);
                items.Add(Print(new MemberExpr(new NameExpr(r) { Type = rty }, "Id").Typed<MemberExpr>(Ty.Int)));
                items.AddRange(inner());
                if (_r.Chance(0.3))
                {
                    items.Add(new IfStmt(BoolExpr(2), new ReturnStmt(ResultLike()), null));
                }
                Pop();
                if (shape == 0)
                {
                    res.Add(new Block(items));
                }
                else
                {
                    res.AddRange(items);
                }
                break;
            }
            case 2:
            {
                Push();
                Declare(r, rty, VK.Local);
                var body = new Block([Print(new MemberExpr(new NameExpr(r) { Type = rty }, "Id").Typed<MemberExpr>(Ty.Int)), .. inner()]);
                Pop();
                res.Add(new UsingBlockStmt(r, mk(), body));
                break;
            }
            case 3:
            {
                // linear move into a consuming helper
                var eat = Callable().FirstOrDefault(f => f.Params.Length == 1 && f.Params[0].Type == rty)
                    ?? AddHelper(Fresh("Eat"), Ty.Int, [(rty, "", "x")], () => new Block([
                        new LocalDeclStmt(null, "g", new NameExpr("x") { Type = rty }, "using"),
                        new ReturnStmt(new MemberExpr(new NameExpr(_r.Chance(0.3) ? "x" : "x") { Type = rty }, "Id").Typed<MemberExpr>(Ty.Int)),
                    ]));
                res.Add(new LocalDeclStmt(_r.Chance(0.5) ? null : rty, r, mk(), _r.Chance(0.2) ? "using" : ""));
                Declare(r, rty, VK.Local);
                res.AddRange(inner());
                res.Add(AccOrPrint(new CallExpr(null, Access(eat), [new Arg("", new NameExpr(r) { Type = rty })]) { Type = Ty.Int }));
                break;
            }
            default:
            {
                // two using declarations: LIFO disposal order
                string r2 = Fresh("r");
                Push();
                var items = new List<Stmt>
                {
                    new LocalDeclStmt(null, r, mk(), "using"),
                    new LocalDeclStmt(null, r2, mk(), "using"),
                };
                Declare(r, rty, VK.Local);
                Declare(r2, rty, VK.Local);
                items.AddRange(inner());
                items.Add(Print(new BinExpr("-", new MemberExpr(new NameExpr(r) { Type = rty }, "Id").Typed<MemberExpr>(Ty.Int), new MemberExpr(new NameExpr(r2) { Type = rty }, "Id").Typed<MemberExpr>(Ty.Int)).Typed<BinExpr>(Ty.Int)));
                Pop();
                res.Add(new Block(items));
                break;
            }
        }
        return res;
    }

    // ───────────── Option / Result ─────────────

    private List<Stmt> POptionResult(Inner inner)
    {
        var res = new List<Stmt>();
        bool isOpt = _r.Chance(0.55);
        var t = isOpt ? Ty.OptInt : Ty.ResIntInt;
        var helper = UnionHelper(t);
        Expr src = Call(helper, 1);
        int shape = _r.Int(6);
        switch (shape)
        {
            case 0:
            case 1:
            {
                // if ([!]x.IsNone(out var v)) / IsErr(out var ok, out var err) guard
                string v = Fresh("v");
                string e = Fresh("er");
                bool negate = _r.Chance(0.4);
                var args = isOpt ? new List<Arg> { new("out", new OutDeclExpr(null, _r.Chance(0.1) ? "_" : v)) }
                                 : [new("out", new OutDeclExpr(null, _r.Chance(0.1) ? "_" : v)), new("out", new OutDeclExpr(null, _r.Chance(0.15) ? "_" : e))];
                Expr guard = new CallExpr(src, isOpt ? "IsNone" : "IsErr", args) { Type = Ty.Bool };
                if (negate)
                {
                    guard = new UnExpr("!", guard).Typed<UnExpr>(Ty.Bool);
                }
                // failure branch diverges (return / Panic) or not
                bool diverge = _r.Chance(0.7);
                Stmt failBody = diverge ? (_r.Chance(0.8) ? new ReturnStmt(ResultLike()) : PanicStmt()) : Print(new Lit("-1", Ty.Int));
                Push();
                Declare(v, Ty.Int, VK.Out);
                var okUse = new Block([AccOrPrint(new NameExpr(v) { Type = Ty.Int }), .. inner()]);
                Pop();
                if (!isOpt && _r.Chance(0.3))
                {
                    Push();
                    Declare(e, Ty.Int, VK.Out);
                    failBody = new Block([Print(new NameExpr(e) { Type = Ty.Int }), failBody]);
                    Pop();
                }
                if (negate)
                {
                    res.Add(new IfStmt(guard, okUse, _r.Chance(0.6) ? failBody : null));
                }
                else
                {
                    res.Add(new IfStmt(guard, failBody, null));
                    Declare(v, Ty.Int, VK.Out, assigned: diverge);
                    res.Add(AccOrPrint(new NameExpr(v) { Type = Ty.Int }));
                    res.AddRange(inner());
                }
                break;
            }
            case 2:
            {
                string m = _r.Weighted((3, "Unwrap"), (3, "UnwrapOr"), (2, "Expect"), (1, "UnwrapOrDefault"));
                var args = m switch
                {
                    "UnwrapOr" => new List<Arg> { new("", Expr(Ty.Int, 2)) },
                    "Expect" => [new("", new Lit("\"expected " + (isOpt ? "some" : "ok") + "\"", Ty.String))],
                    _ => [],
                };
                res.AddRange(inner());
                res.Add(AccOrPrint(new CallExpr(src, m, args) { Type = Ty.Int }));
                break;
            }
            case 3:
            {
                // is-pattern on a union
                string v = Fresh("v");
                var pat = isOpt ? new UnionPat("Some<int>", new VarPat(v)) : new UnionPat(_r.Chance(0.7) ? "Ok<int>" : "Err<int>", new VarPat(v));
                string loc = Fresh("u");
                Declare(loc, t, VK.Local);
                res.Add(new LocalDeclStmt(_r.Chance(0.5) ? t : null, loc, src, ""));
                Push();
                Declare(v, Ty.Int, VK.Pattern);
                var then = new Block([AccOrPrint(new NameExpr(v) { Type = Ty.Int }), .. inner()]);
                Pop();
                res.Add(new IfStmt(new IsPatExpr(new NameExpr(loc) { Type = t }, pat).Typed<IsPatExpr>(Ty.Bool), then, _r.Chance(0.4) ? Print(new Lit("0", Ty.Int)) : null));
                break;
            }
            case 4:
            {
                string loc = Fresh("u");
                Declare(loc, t, VK.Local);
                res.Add(new LocalDeclStmt(t, loc, src, ""));
                res.AddRange(inner());
                if (isOpt)
                {
                    res.Add(Print(new CallExpr(new NameExpr(loc) { Type = t }, _r.Chance(0.5) ? "IsSome" : "IsEmpty", []) { Type = Ty.Bool }));
                }
                else
                {
                    res.Add(Print(new CallExpr(new NameExpr(loc) { Type = t }, "IsOk", []) { Type = Ty.Bool }));
                }
                break;
            }
            default:
            {
                // union switch statement/expression handled by PSwitchStmt selectors; here: local then switch expr
                string loc = Fresh("u");
                Declare(loc, t, VK.Local);
                res.Add(new LocalDeclStmt(t, loc, src, ""));
                res.AddRange(inner());
                var e = SwitchExprProd(Ty.Int, 1);
                if (e is not null)
                {
                    res.Add(AccOrPrint(e));
                }
                break;
            }
        }
        return res;
    }

    // ───────────── Panic ─────────────

    private Stmt PanicStmt() => new ExprStmt(new CallExpr(null, "VibraniumRuntime.Panic", [new Arg("", new Lit("\"" + _r.Pick(["boom", "stop", "bad state"]) + "\"", Ty.String))]));

    private List<Stmt> PPanic(Inner inner)
    {
        var res = inner();
        switch (_r.Int(4))
        {
            case 0:
                res.Add(new IfStmt(BoolExpr(1), PanicStmt(), null));
                break;
            case 1:
            {
                // a helper that panics and then needs (or omits) a return
                bool needsReturn = _r.Chance(0.75);
                var h = AddHelper(Fresh("Fail"), Ty.Int, [(Ty.Int, "", "k")], () => new Block(needsReturn
                    ? [new IfStmt(new BinExpr(">", new NameExpr("k") { Type = Ty.Int }, IntLit(Ty.Int, 0, 9)).Typed<BinExpr>(Ty.Bool), PanicStmt(), null), new ReturnStmt(new NameExpr("k") { Type = Ty.Int })]
                    : [PanicStmt()]));
                res.Add(AccOrPrint(new CallExpr(null, Access(h), [new Arg("", Expr(Ty.Int, 1))]) { Type = Ty.Int }));
                break;
            }
            case 2:
                res.Add(Print(Expr(Ty.Int, 1)));
                res.Add(new IfStmt(BoolExpr(1), new Block([PanicStmt(), new ReturnStmt(ResultLike())]), null));
                break;
            default:
            {
                // Panic followed by code that relies on source continuation
                string x = Fresh("x");
                res.Add(new LocalDeclStmt(Ty.Int, x, null, ""));
                Declare(x, Ty.Int, VK.Local);
                res.Add(new IfStmt(BoolExpr(1), new AssignStmt(new NameExpr(x) { Type = Ty.Int }, "=", Expr(Ty.Int, 1)), PanicStmt()));
                res.Add(AccOrPrint(new NameExpr(x) { Type = Ty.Int }));
                break;
            }
        }
        return res;
    }

    // ───────────── arithmetic ─────────────

    private List<Stmt> PArith(Inner inner)
    {
        var res = inner();
        var t = _r.Weighted((3, Ty.Int), (2, Ty.Long), (2, Ty.Double), (2, Ty.Float), (1, Ty.UInt), (1, Ty.UShort), (1, Ty.Byte), (1, Ty.ULong));
        string n = Fresh("w");
        // runtime value derived from parameters
        var src = Readable(v => v == Ty.Int || v == Ty.Long);
        Expr init = src.Count > 0 ? new CastExpr(t, Paren(new BinExpr("+", VarRef(_r.Pick(src)), IntLit(src[0].Type, 1, 9)).Typed<BinExpr>(src[0].Type))).Typed<CastExpr>(t) : Expr(t, 1);
        Declare(n, t, VK.Local);
        res.Add(new LocalDeclStmt(t, n, init, ""));
        var nv = new NameExpr(n) { Type = t };
        string op = _r.Weighted((4, "%"), (2, "/"), (2, "*"), (1, "-"), (1, "<<"), (1, ">>"));
        if (t.IsFloating && op is "<<" or ">>")
        {
            op = "%";
        }
        Expr rhs = op is "<<" or ">>" ? IntLit(Ty.Int, 0, 40) : _r.Chance(0.6) ? IntLit(t.PromotesToInt ? Ty.Int : t, -3, 7) : Expr(t, 2);
        if (t.PromotesToInt && rhs.Type != Ty.Int)
        {
            rhs = new CastExpr(Ty.Int, rhs).Typed<CastExpr>(Ty.Int);
        }
        Expr calc = new BinExpr(op, nv, Paren(rhs)).Typed<BinExpr>(t.ArithResult);
        if (_r.Chance(0.3) && !t.PromotesToInt)
        {
            res.Add(new AssignStmt(new NameExpr(n) { Type = t }, op + "=", rhs));
            calc = new NameExpr(n) { Type = t };
        }
        Expr shown = calc.Type is { IsPrintable: true } ? calc : new CastExpr(_r.Chance(0.7) ? Ty.Int : Ty.Long, Paren(calc)).Typed<CastExpr>(Ty.Int);
        res.Add(Print(shown));
        return res;
    }

    // ───────────── pattern variables ─────────────

    private List<Stmt> PPatternVar(Inner inner)
    {
        var res = new List<Stmt>();
        var ints = ReadableV(v => v.Type == Ty.Int);
        if (ints.Count == 0)
        {
            var d = LocalDecl(Ty.Int);
            res.Add(d);
            ints = [Lookup(d.Name)!];
        }
        string q = Fresh("q");
        var subj = VarRef(_r.Pick(ints));
        // `x is > k and var q` is cut C20: the relational test moves into `&& q > k`
        int pk = _r.Int(3);
        Pat pat = pk == 0 ? new DeclPat(Ty.Int, q) : new VarPat(q);
        Expr cond = new IsPatExpr(subj, pat).Typed<IsPatExpr>(Ty.Bool);
        if (pk == 1 || _r.Chance(0.5))
        {
            cond = new BinExpr("&&", cond, new BinExpr(pk == 1 ? ">" : _r.Pick([">", "<", "!="]), new NameExpr(q) { Type = Ty.Int }, IntLit(Ty.Int, pk == 1 ? -2 : 0, pk == 1 ? 3 : 5)).Typed<BinExpr>(Ty.Bool)).Typed<BinExpr>(Ty.Bool);
        }
        bool loop = _r.Chance(0.2);
        Push();
        Declare(q, Ty.Int, VK.Pattern);
        var then = new Block([AccOrPrint(new NameExpr(q) { Type = Ty.Int }), .. inner()]);
        if (loop)
        {
            then.Items.Add(new BreakStmt());
        }
        Pop();
        res.Add(loop ? new WhileStmt(cond, then) : new IfStmt(cond, then, null));
        if (_r.Chance(0.25))
        {
            // scope/definite-assignment leak: the pattern variable after the statement
            res.Add(Print(new NameExpr(q) { Type = Ty.Int }));
        }
        return res;
    }

    // ───────────── class instances ─────────────

    private List<Stmt> PClassInstance(Inner inner)
    {
        var res = new List<Stmt>();
        string cn = Fresh("C");
        bool primary = _r.Chance(0.6);
        // a field named `_` is cut C11
        string fieldName = "F";
        var members = new List<Node>
        {
            new FieldDecl(Ty.Int, fieldName, primary ? new NameExpr("seed") : IntLit(Ty.Int, 1, 9), "public"),
        };
        // instance method body generated with the field visible
        var saveScope = _scope;
        var saveFn = _fn;
        var saveContainer = _container;
        var cls = ClassScope(cn);
        cls.Vars.Add(new Var(fieldName, Ty.Int, VK.Field) { Mutable = true });
        _scope = new Scope(cls);
        bool mut = _r.Chance(0.5);
        _container = cn;
        _fn = new FnState(Ty.Int, cn) { InStaticLocal = false, Acc = null };
        var body = new List<Stmt>();
        body.AddRange(inner());
        if (_r.Chance(0.6))
        {
            body.Add(new ExprStmt(new CallExpr(null, "int.TryParse", [new Arg("", new Lit("\"" + _r.Range(10, 60) + "\"", Ty.String)), new Arg("out", new NameExpr(fieldName))])));
        }
        else if (_r.Chance(0.5))
        {
            body.Add(new AssignStmt(new NameExpr(fieldName) { Type = Ty.Int }, _r.Pick(["=", "+="]), Expr(Ty.Int, 1)));
        }
        body.Add(new ReturnStmt(new NameExpr(fieldName) { Type = Ty.Int }));
        _scope = saveScope;
        _fn = saveFn;
        _container = saveContainer;
        members.Add(new FuncDecl(mut ? "[Mut] public" : "public", Ty.Int, "Run", new ParamList([]), new Block(body), null, false));
        members.AddRange(ClassMembers(cn));
        _types.Add(new ClassDecl(cn, "sealed", primary ? "int seed" : null, null, members));
        string local = Fresh("c");
        var cty = Ty.Named(TK.Class, cn);
        res.Add(new LocalDeclStmt(_r.Chance(0.5) ? null : cty, local, new NewExpr(cn, primary ? [new Arg("", Expr(Ty.Int, 1))] : []) { Type = cty }, ""));
        Declare(local, cty, VK.Local);
        res.Add(AccOrPrint(new CallExpr(new NameExpr(local) { Type = cty }, "Run", []) { Type = Ty.Int }));
        return res;
    }
}
