using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>
/// Activation-profile anchors (<c>--profile activation</c>), from the Round-11 audit and the call-activation model:
/// a borrow and a move of related places in one call or assignment (loan kinds × move forms × place relations × call
/// forms), views returned through parameters under <c>[return: LifetimeOf]</c> and elision, conditional temporaries in
/// argument position, constant float → <c>nint</c>/<c>nuint</c> conversions, Std append to a pipe, the declaration cuts
/// C5–C9, and the generated-code marker. The scheduler pairs them only within the activation profile, so default and
/// extended (seed, index) programs are unchanged.
/// Every borrow-plus-move scenario lives in its own helper function and starts at the top of its body, so the conflict is
/// always reachable: SPEC-invalid scenarios carry a <c>vib-reject</c> tag whose expected ID the engine checks.
/// </summary>
internal sealed partial class Gen
{
    /// <summary>Generator profile; program-level activation choices (generated-code marker) apply only to "activation".</summary>
    public string Profile { get; init; } = "default";

    /// <summary>Adds a helper function (top-level static local function, or public static member of the container class).</summary>
    private string ActHelper(string prefix, Func<string, string> text, string attr = "", params string[] tags)
    {
        string name = Fresh(prefix);
        string t = text(name);
        string a = attr.Length > 0 ? attr + " " : "";
        if (_container == "top")
        {
            _topHelpers.Add(Raw(a + "static " + t, tags));
        }
        else
        {
            ClassMembers(_container).Add(Raw(a + "public static " + t, tags));
        }
        return name;
    }

    private void ActType(string text, params string[] tags) => _types.Add(Raw(text, tags));

    /// <summary>Places the inner anchor before or after the activation statements.</summary>
    private List<Stmt> WithInner(Inner inner, List<Stmt> own)
    {
        var res = new List<Stmt>();
        bool before = _r.Chance(0.5);
        if (before)
        {
            res.AddRange(inner());
        }
        res.AddRange(own);
        if (!before)
        {
            res.AddRange(inner());
        }
        return res;
    }

    // ───────────── call activation: owners and places ─────────────

    /// <summary>One owner kind of a borrow-plus-move scenario.</summary>
    private sealed class CaOwner
    {
        public string Kind = "";     // vec | node | holder | option | linear
        public string Type = "";     // C# type of the root
        public string NodeT = "";    // node class (node, holder, option)
        public string HolderT = "";  // holder class
        public string LinT = "";     // linear class
        public Func<string, string, string> Decl = (_, _) => "";
    }

    private CaOwner CaNewOwner(string kind)
    {
        var o = new CaOwner { Kind = kind };
        switch (kind)
        {
            case "vec":
                o.Type = "Vec<int>";
                o.Decl = (x, init) => $"Vec<int> {x} = [{init}, 2];";
                break;
            case "node":
                o.NodeT = Fresh("Nd");
                o.Type = o.NodeT;
                ActType($"public sealed class {o.NodeT}(int v) {{ public int V = v; public int Peek({o.NodeT} o) {{ int k = Sink(o); return V + k; }} [Mut] public void Absorb({o.NodeT} o) {{ int k = Sink(o); V = V + k; }} static int Sink({o.NodeT} x) => x.V; }}", "ca.owner-class");
                o.Decl = (x, init) => $"var {x} = new {o.NodeT}({init});";
                break;
            case "holder":
                o.NodeT = Fresh("Nd");
                o.HolderT = Fresh("Hd");
                o.Type = o.HolderT;
                ActType($"public sealed class {o.NodeT}(int v) {{ public int V = v; public int Peek({o.HolderT} o) {{ int k = Sink(o); return V + k; }} [Mut] public void Absorb({o.HolderT} o) {{ int k = Sink(o); V = V + k; }} static int Sink({o.HolderT} x) => x.N.V; }}", "ca.owner-holder");
                ActType($"public sealed class {o.HolderT}({o.NodeT} n, Vec<int> items) {{ public {o.NodeT} N = n; public Vec<int> Items = items; }}", "ca.owner-holder");
                o.Decl = (x, init) => $"var {x} = new {o.HolderT}(new {o.NodeT}({init}), [{init}, 2]);";
                break;
            case "option":
                o.NodeT = Fresh("Nd");
                o.Type = $"Option<{o.NodeT}>";
                ActType($"public sealed class {o.NodeT}(int v) {{ public int V = v; }}", "ca.owner-option");
                o.Decl = (x, init) => $"Option<{o.NodeT}> {x} = Some(new {o.NodeT}({init}));";
                break;
            default:
                o.LinT = Fresh("Rs");
                o.Type = o.LinT;
                ActType($"public sealed class {o.LinT}(int id) : IDisposable {{ public int Id = id; public void Dispose() {{ WriteLine(Id); }} }}", "ca.owner-linear");
                o.Decl = (x, init) => $"var {x} = new {o.LinT}({init});";
                break;
        }
        return o;
    }

    /// <summary>A value read of parameter <paramref name="a"/> of type kind <paramref name="tk"/>.</summary>
    private static string CaRead(string tk, string a) => tk switch
    {
        "vec" => $"({a}.Count > 0 ? {a}[0] : -1)",
        "span" => $"({a}.Length > 0 ? {a}[0] : -1)",
        "node" => $"{a}.V",
        "holder" => $"{a}.N.V",
        "option" or "optview" => $"({a}.IsSome ? 1 : 0)",
        "linear" => $"{a}.Id",
        _ => "0",
    };

    /// <summary>The body of a static sink that consumes a by-value parameter <c>x</c> of type kind <paramref name="tk"/>.</summary>
    private static string CaSinkBody(string tk, string type) => tk switch
    {
        "linear" => $"({type} x) {{ int k = x.Id; using var d = x; return k; }}",
        _ => $"({type} x) => {CaRead(tk, "x")};",
    };

    private string CaFreshValue(CaOwner o, string tk) => tk switch
    {
        "vec" => "[7, 8]",
        "node" => $"new {o.NodeT}(7)",
        "holder" => $"new {o.HolderT}(new {o.NodeT}(7), [7])",
        "option" => $"Some(new {o.NodeT}(7))",
        _ => "default",
    };

    // ───────────── call activation: argument loans (static, local, instance, constructor) ─────────────

    private List<Stmt> PCaCall(Inner inner)
    {
        string helper = CaArgScenario();
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    private string CaArgScenario()
    {
        string kind = _r.Weighted((5, "vec"), (4, "node"), (4, "holder"), (3, "option"), (2, "linear"));
        var o = CaNewOwner(kind);
        var tags = new List<string> { "ca.owner-" + (kind == "node" ? "class" : kind) };
        // relation and the loaned / moved places
        string rel = kind == "holder" ? _r.Weighted((4, "ext"), (3, "prefix"), (2, "unrelated")) : _r.Weighted((5, "same"), (2, "unrelated"));
        string x = "x", y = rel == "unrelated" ? "y" : "x";
        string loanPlace, loanTk, movePlace, moveTk;
        if (kind == "holder")
        {
            bool viaNode = _r.Chance(0.5);
            if (rel == "prefix")
            {
                loanPlace = x;
                loanTk = "holder";
                movePlace = viaNode ? $"{x}.N" : $"{x}.Items";
                moveTk = viaNode ? "node" : "vec";
            }
            else
            {
                loanPlace = viaNode ? $"{x}.N" : $"{x}.Items";
                loanTk = viaNode ? "node" : "vec";
                movePlace = y;
                moveTk = "holder";
            }
        }
        else
        {
            loanPlace = x;
            loanTk = kind;
            movePlace = y;
            moveTk = kind;
        }
        tags.Add("ca.rel-" + rel);
        bool root = !loanPlace.Contains('.', StringComparison.Ordinal);
        string[] loans = loanTk switch
        {
            // `ref readonly` parameters are cut C10: an `in` parameter with an implicit by-reference argument instead
            "vec" => root ? ["in", "in-implicit", "in", "ref", "out", "view-ros", "view-span", "view-slice", "view-ros-slice"] : ["in", "in-implicit", "ref", "view-ros", "view-span"],
            "node" => root ? ["in", "in-implicit", "in", "ref", "out"] : ["in", "in-implicit", "ref"],
            "holder" => ["in", "in-implicit", "ref"],
            "option" => ["in", "ref", "view-mut", "view-ref"],
            _ => ["in", "in-implicit"],
        };
        string loan = _r.Pick(loans);
        string loanType = loanTk switch { "vec" => "Vec<int>", "node" => o.NodeT, "holder" => o.HolderT, "option" => o.Type, _ => o.Type };
        (string arg, string param, string readTk) = loan switch
        {
            "in" => ($"in {loanPlace}", $"in {loanType} a", loanTk),
            "in-implicit" => (loanPlace, $"in {loanType} a", loanTk),
            "ref" => ($"ref {loanPlace}", $"ref {loanType} a", loanTk),
            "out" => ($"out {loanPlace}", $"out {loanType} a", loanTk),
            "view-ros" => ($"{loanPlace}.AsReadOnlySpan()", "ReadOnlySpan<int> a", "span"),
            "view-span" => ($"{loanPlace}.AsSpan()", "Span<int> a", "span"),
            "view-slice" => ($"{loanPlace}[0..1]", "Span<int> a", "span"),
            "view-ros-slice" => ($"{loanPlace}.AsReadOnlySpan()[0..1]", "ReadOnlySpan<int> a", "span"),
            "view-mut" => ($"{loanPlace}.AsMut()", $"OptionMut<{o.NodeT}> a", "optview"),
            _ => ($"{loanPlace}.AsRef()", $"OptionRef<{o.NodeT}> a", "optview"),
        };
        tags.Add(loan switch
        {
            "in" => "ca.loan-in",
            "in-implicit" => "ca.loan-in-implicit",
            "ref" => "ca.loan-ref",
            "out" => "ca.loan-out",
            _ => "ca.loan-view",
        });
        if (!root && loan is "in" or "in-implicit" or "ref")
        {
            tags.Add("ca.loan-ref-proj");
        }
        // move form
        string moveType = moveTk switch { "vec" => "Vec<int>", "node" => o.NodeT, "holder" => o.HolderT, _ => o.Type };
        string mf = kind == "option" && movePlace == y && !movePlace.Contains('.', StringComparison.Ordinal) ? _r.Weighted((3, "bare"), (2, "explicit"), (2, "consume")) : _r.Weighted((3, "bare"), (2, "explicit"));
        string moveArg = mf switch { "bare" => movePlace, "explicit" => $"Move({movePlace})", _ => $"{movePlace}.Unwrap()" };
        string sinkTk = moveTk;
        if (mf == "consume")
        {
            moveType = o.NodeT;
            sinkTk = "node";
        }
        tags.Add("ca.move-" + mf);
        // order: the borrow first (CA rule 4), or the move first (rule 5)
        bool moveFirst = loan != "out" && rel != "prefix" && _r.Chance(0.25);
        tags.Add(rel == "unrelated" ? "ca.control-unrelated" : moveFirst ? "ca.move-then-use" : "ca.borrow-then-move");
        // named arguments stay allowed in their own position (out-of-order names are cut C13)
        bool named = _r.Chance(0.2);
        if (named)
        {
            tags.Add("ca.named-in-position");
        }
        string na = named ? "a: " : "", nb = named ? "b: " : "";
        string args = moveFirst ? $"{nb}{moveArg}, {na}{arg}" : $"{na}{arg}, {nb}{moveArg}";
        string pars = moveFirst ? $"{moveType} b, {param}" : $"{param}, {moveType} b";
        string body = loan == "out"
            ? $"{{ int k = Sink(b); a = {CaFreshValue(o, loanTk)}; return k; }}"
            : $"{{ int k = Sink(b); return {CaRead(readTk, "a")} + k; }}";
        string sinkDef = CaSinkBody(sinkTk, moveType);
        // declarations
        var decls = new List<string> { o.Decl(x, "p") };
        if (y != x)
        {
            decls.Add(o.Decl(y, "p + 1"));
        }
        // the call form
        string form = _r.Weighted((3, "local"), (3, "static"), (2, "instance"), (2, "ctor"));
        tags.Add("ca.form-" + form);
        string call;
        string tail = "";
        switch (form)
        {
            case "local":
                call = $"Eat({args})";
                tail = $" static int Eat({pars}) {body} static int Sink{sinkDef}";
                break;
            case "static":
            {
                string sink = ActHelper("Sink", n => $"int {n}{sinkDef}");
                string eat = ActHelper("Eat", n => $"int {n}({pars}) {body.Replace("Sink(", sink + "(", StringComparison.Ordinal)}");
                call = $"{eat}({args})";
                break;
            }
            case "instance":
            {
                string k = Fresh("Ck");
                ActType($"public sealed class {k}(int z) {{ public int Z = z; public int Eat({pars}) {body} static int Sink{sinkDef} }}");
                decls.Add($"var c = new {k}(p);");
                call = $"c.Eat({args})";
                break;
            }
            default:
            {
                string w = Fresh("Cw");
                string ctorBody = loan == "out"
                    ? $"{{ int t = Sink(b); a = {CaFreshValue(o, loanTk)}; K = t; }}"
                    : $"{{ int t = Sink(b); K = {CaRead(readTk, "a")} + t; }}";
                ActType($"public sealed class {w} {{ public int K; public {w}({pars}) {ctorBody} static int Sink{sinkDef} }}");
                call = $"new {w}({args}).K";
                break;
            }
        }
        // after the call: controls read the loaned owner again; a linear owner left over is disposed
        string post = "";
        string ret = "r";
        if (rel == "unrelated")
        {
            if (kind == "linear")
            {
                post = $" using var d = {x};";
            }
            else
            {
                ret = "r + " + CaRead(kind == "holder" ? "holder" : kind, x);
            }
        }
        string text = $"int {{0}}(int p) {{ {string.Join(" ", decls)} int r = {call};{post} return {ret};{tail} }}";
        return ActHelper("Ca", n => string.Format(System.Globalization.CultureInfo.InvariantCulture, text.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal).Replace("{{0}}", "{0}", StringComparison.Ordinal), n), "", [.. tags]);
    }

    // ───────────── call activation: receivers, indexers and operators ─────────────

    private List<Stmt> PCaReceiver(Inner inner)
    {
        string helper = CaReceiverScenario();
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    private string CaReceiverScenario()
    {
        var tags = new List<string>();
        string form = _r.Weighted((3, "shared"), (3, "mut"), (2, "rstruct"), (2, "this-in"), (2, "this-ref"), (3, "indexer"), (2, "operator"));
        bool unrelated = _r.Chance(0.3);
        bool explicitMove = _r.Chance(0.4);
        string M(string place) => explicitMove ? $"Move({place})" : place;
        tags.Add(explicitMove ? "ca.move-explicit" : "ca.move-bare");
        var decls = new List<string>();
        string stmt;
        string ret;
        string rel;
        switch (form)
        {
            case "shared":
            case "mut":
            {
                tags.Add(form == "shared" ? "ca.loan-shared-recv" : "ca.loan-mut-recv");
                tags.Add("ca.form-instance");
                string meth = form == "shared" ? "Peek" : "Absorb";
                bool viaHolder = !unrelated && _r.Chance(0.35);
                var o = CaNewOwner(viaHolder ? "holder" : "node");
                tags.Add(viaHolder ? "ca.owner-holder" : "ca.owner-class");
                decls.Add(o.Decl("x", "p"));
                if (viaHolder)
                {
                    rel = "ext";
                    stmt = form == "shared" ? $"int r = x.N.Peek({M("x")});" : $"x.N.Absorb({M("x")}); int r = 1;";
                    ret = "r";
                }
                else if (unrelated)
                {
                    rel = "unrelated";
                    decls.Add(o.Decl("y", "p + 1"));
                    stmt = form == "shared" ? $"int r = x.Peek({M("y")});" : $"x.Absorb({M("y")}); int r = x.V;";
                    ret = "r + x.V";
                }
                else
                {
                    rel = "same";
                    stmt = form == "shared" ? $"int r = x.Peek({M("x")});" : $"x.Absorb({M("x")}); int r = 1;";
                    ret = "r";
                }
                _ = meth;
                break;
            }
            case "rstruct":
            {
                tags.Add("ca.loan-shared-recv");
                tags.Add("ca.owner-struct");
                tags.Add("ca.form-instance");
                string s = Fresh("Rst");
                ActType($"public readonly struct {s}(Vec<int> v) {{ readonly Vec<int> V = v; public int R({s} o) {{ int k = Sink(o); return V.Count > 0 ? V[0] + k : k; }} static int Sink({s} x) => 1; }}", "ca.owner-struct");
                decls.Add($"var x = new {s}([p, 2]);");
                if (unrelated)
                {
                    rel = "unrelated";
                    decls.Add($"var y = new {s}([p + 1, 3]);");
                    stmt = $"int r = x.R({M("y")});";
                    ret = "r + x.R(new " + s + "([4]))";
                }
                else
                {
                    rel = "same";
                    stmt = $"int r = x.R({M("x")});";
                    ret = "r";
                }
                break;
            }
            case "this-in":
            case "this-ref":
            {
                tags.Add(form == "this-in" ? "ca.loan-this-in" : "ca.loan-this-ref");
                tags.Add("ca.form-extension");
                string mod = form == "this-in" ? "this in" : "this ref";
                bool viaHolder = !unrelated && _r.Chance(0.35);
                string ext = Fresh("Ext");
                if (viaHolder)
                {
                    var o = CaNewOwner("holder");
                    tags.Add("ca.owner-holder");
                    ActType($"public static class {ext} {{ public static int Look({mod} Vec<int> a, {o.HolderT} b) {{ int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; }} static int Sink({o.HolderT} x) => x.N.V; }}");
                    decls.Add(o.Decl("x", "p"));
                    rel = "ext";
                    stmt = $"int r = x.Items.Look({M("x")});";
                    ret = "r";
                }
                else
                {
                    tags.Add("ca.owner-vec");
                    ActType($"public static class {ext} {{ public static int Look({mod} Vec<int> a, Vec<int> b) {{ int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; }} static int Sink(Vec<int> x) => x.Count; }}");
                    decls.Add("Vec<int> x = [p, 2];");
                    if (unrelated)
                    {
                        rel = "unrelated";
                        decls.Add("Vec<int> y = [p + 1, 3];");
                        stmt = $"int r = x.Look({M("y")});";
                        ret = "r + x.Count";
                    }
                    else
                    {
                        rel = "same";
                        stmt = $"int r = x.Look({M("x")});";
                        ret = "r";
                    }
                }
                break;
            }
            case "indexer":
            {
                tags.Add("ca.loan-shared-recv");
                tags.Add("ca.form-indexer-get");
                bool viaHolder = !unrelated && _r.Chance(0.35);
                if (viaHolder)
                {
                    var o = CaNewOwner("holder");
                    tags.Add("ca.owner-holder");
                    string idx = ActHelper("Idx", n => $"int {n}({o.HolderT} b) => b.Items.Count - 1;");
                    decls.Add(o.Decl("x", "p"));
                    rel = "ext";
                    stmt = $"int r = x.Items[{idx}({M("x")})];";
                    ret = "r";
                }
                else
                {
                    tags.Add("ca.owner-vec");
                    string idx = ActHelper("Idx", n => $"int {n}(Vec<int> b) => b.Count - 1;");
                    decls.Add("Vec<int> x = [p, 2];");
                    if (unrelated)
                    {
                        rel = "unrelated";
                        decls.Add("Vec<int> y = [p + 1, 3];");
                        stmt = $"int r = x[{idx}({M("y")})];";
                        ret = "r + x.Count";
                    }
                    else
                    {
                        rel = "same";
                        stmt = $"int r = x[{idx}({M("x")})];";
                        ret = "r";
                    }
                }
                break;
            }
            default:
            {
                tags.Add("ca.loan-mut-recv");
                tags.Add("ca.form-operator");
                tags.Add("ca.owner-class");
                string b = Fresh("Bg");
                ActType($"public sealed class {b}(int n) {{ public int N = n; [Mut] public void operator +=({b} o) {{ N += Sink(o); }} static int Sink({b} x) => x.N; }}", "ca.owner-class");
                decls.Add($"var x = new {b}(p);");
                if (unrelated)
                {
                    rel = "unrelated";
                    decls.Add($"var y = new {b}(p + 1);");
                    stmt = $"x += {M("y")}; int r = x.N;";
                    ret = "r";
                }
                else
                {
                    rel = "same";
                    stmt = $"x += {M("x")}; int r = 1;";
                    ret = "r";
                }
                break;
            }
        }
        tags.Add("ca.rel-" + rel);
        tags.Add(rel == "unrelated" ? "ca.control-unrelated" : "ca.borrow-then-move");
        string text = $"(int p) {{ {string.Join(" ", decls)} {stmt} return {ret}; }}";
        return ActHelper("Cr", n => $"int {n}{text}", "", [.. tags]);
    }

    // ───────────── call activation: assignment targets ─────────────

    private List<Stmt> PCaAssign(Inner inner)
    {
        string helper = CaAssignScenario();
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    private string CaAssignScenario()
    {
        var tags = new List<string> { "ca.loan-target" };
        string form = _r.Weighted((3, "index"), (2, "compound"), (3, "ref-local"), (3, "field"), (2, "int-field"));
        bool explicitMove = _r.Chance(0.4);
        string M(string place) => explicitMove ? $"Move({place})" : place;
        int ctl = _r.Int(10); // 0-2 unrelated, 3-4 shared read / own slot, else related
        var decls = new List<string>();
        string stmt;
        string ret = "1";
        string expect;
        switch (form)
        {
            case "index":
            case "compound":
            {
                tags.Add(form == "index" ? "ca.form-indexer-set" : "ca.form-compound-index");
                string op = form == "index" ? "=" : "+=";
                bool viaHolder = ctl >= 5 && _r.Chance(0.35);
                if (viaHolder)
                {
                    var o = CaNewOwner("holder");
                    tags.Add("ca.owner-holder");
                    string len = ActHelper("Len", n => $"int {n}({o.HolderT} b) => b.Items.Count;");
                    decls.Add(o.Decl("x", "p"));
                    stmt = $"x.Items[0] {op} {len}({M("x")});";
                    expect = "ca.borrow-then-move";
                    tags.Add("ca.rel-ext");
                }
                else
                {
                    tags.Add("ca.owner-vec");
                    // helpers are added only when used (an unused local function is CS8321)
                    string Len() => ActHelper("Len", n => $"int {n}(Vec<int> b) => b.Count;");
                    decls.Add("Vec<int> x = [p, 2];");
                    if (ctl <= 2)
                    {
                        decls.Add("Vec<int> y = [p + 1, 3];");
                        stmt = $"x[0] {op} {Len()}({M("y")});";
                        ret = "x[0]";
                        expect = "ca.control-unrelated";
                        tags.Add("ca.rel-unrelated");
                    }
                    else if (ctl <= 4)
                    {
                        stmt = $"x[0] {op} x.Count + x[1];";
                        ret = "x[0]";
                        expect = "ca.control-shared-read-reserved";
                        tags.Add("ca.rel-same");
                    }
                    else
                    {
                        stmt = $"x[0] {op} {Len()}({M("x")});";
                        expect = "ca.borrow-then-move";
                        tags.Add("ca.rel-same");
                    }
                }
                break;
            }
            case "ref-local":
            {
                tags.Add("ca.form-assign-ref");
                tags.Add("ca.owner-vec");
                decls.Add("Vec<int> x = [p, 2];");
                if (ctl <= 2)
                {
                    decls.Add("Vec<int> y = [p + 1, 3];");
                    stmt = "ref var t = ref x; t = Move(y);";
                    ret = "x.Count + x[0]";
                    expect = "ca.control-unrelated";
                    tags.Add("ca.rel-unrelated");
                }
                else if (ctl <= 4)
                {
                    stmt = "x = Move(x);";
                    ret = "x.Count + x[0]";
                    expect = "ca.control-own-slot";
                    tags.Add("ca.rel-same");
                }
                else
                {
                    stmt = "ref var t = ref x; t = Move(x);";
                    expect = "ca.borrow-then-move";
                    tags.Add("ca.rel-same");
                }
                break;
            }
            case "field":
            {
                tags.Add("ca.form-assign-field");
                var o = CaNewOwner("holder");
                tags.Add("ca.owner-holder");
                string Take() => ActHelper("Take", n => $"{o.NodeT} {n}({o.HolderT} b) => new {o.NodeT}(b.N.V + 1);");
                decls.Add(o.Decl("x", "p"));
                if (ctl <= 2)
                {
                    decls.Add(o.Decl("y", "p + 1"));
                    stmt = $"x.N = {Take()}({M("y")});";
                    ret = "x.N.V";
                    expect = "ca.control-unrelated";
                    tags.Add("ca.rel-unrelated");
                }
                else if (ctl <= 4)
                {
                    stmt = $"x.N = new {o.NodeT}(x.N.V + 1);";
                    ret = "x.N.V";
                    expect = "ca.control-shared-read-reserved";
                    tags.Add("ca.rel-prefix");
                }
                else
                {
                    stmt = $"x.N = {Take()}({M("x")});";
                    expect = "ca.borrow-then-move";
                    tags.Add("ca.rel-prefix");
                }
                break;
            }
            default:
            {
                tags.Add("ca.form-assign-field");
                var o = CaNewOwner("holder");
                tags.Add("ca.owner-holder");
                string Peek() => ActHelper("PeekH", n => $"int {n}({o.HolderT} b) => b.N.V;");
                decls.Add(o.Decl("x", "p"));
                if (ctl <= 2)
                {
                    decls.Add(o.Decl("y", "p + 1"));
                    stmt = $"x.N.V = {Peek()}({M("y")});";
                    ret = "x.N.V";
                    expect = "ca.control-unrelated";
                    tags.Add("ca.rel-unrelated");
                }
                else if (ctl <= 4)
                {
                    stmt = "x.N.V = x.N.V + x.Items.Count;";
                    ret = "x.N.V";
                    expect = "ca.control-shared-read-reserved";
                    tags.Add("ca.rel-prefix");
                }
                else
                {
                    stmt = $"x.N.V = {Peek()}({M("x")});";
                    expect = "ca.borrow-then-move";
                    tags.Add("ca.rel-prefix");
                }
                break;
            }
        }
        if (expect is "ca.borrow-then-move" or "ca.control-unrelated")
        {
            tags.Add(explicitMove ? "ca.move-explicit" : "ca.move-bare");
        }
        tags.Add(expect);
        string text = $"(int p) {{ {string.Join(" ", decls)} {stmt} return {ret}; }}";
        return ActHelper("As", n => $"int {n}{text}", "", [.. tags]);
    }

    // ───────────── views returned through parameters ─────────────

    private List<Stmt> PViewReturn(Inner inner)
    {
        var tags = new List<string>();
        string body;
        switch (_r.Weighted((3, 0), (2, 1), (2, 2), (1, 3), (2, 4), (2, 5)))
        {
            case 0:
            {
                // [return: LifetimeOf("x")]: the call site may mutate the other input while the view lives
                int v = _r.Int(4);
                string ret = v switch { 0 => "f ? x : y", 1 => "x", 2 => "f ? x : x[..1]", _ => "f ? x : y" };
                string attr = v == 3 ? "[return: LifetimeOf(\"x\", \"y\")]" : "[return: LifetimeOf(\"x\")]";
                string pick = ActHelper("Pick", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x, ReadOnlySpan<int> y, bool f) => {ret};", attr,
                    v == 0 ? "view.lifetimeof-violated" : v == 3 ? "view.lifetimeof-two" : "view.lifetimeof");
                bool mutate = _r.Chance(0.7);
                string mut = mutate ? (v == 3 && _r.Chance(0.5) ? "a.Add(5); " : "b.Add(5); ") : "";
                if (v == 3 && mutate)
                {
                    tags.Add("view.use-after-mutate");
                }
                else if (mutate && mut.StartsWith('b'))
                {
                    tags.Add("view.call-site-other-mutated");
                }
                if (mut.StartsWith('a'))
                {
                    tags.Remove("view.use-after-mutate");
                    tags.Add("view.use-after-mutate");
                }
                body = $"Vec<int> a = [p, 2]; Vec<int> b = [3, 4]; var s = {pick}(a.AsReadOnlySpan(), b.AsReadOnlySpan(), p > 0); {mut}return s[0];";
                break;
            }
            case 1:
            {
                // elision to `this` on an instance method with a view parameter
                bool bad = _r.Chance(0.4);
                string g = Fresh("Gv");
                ActType($"public sealed class {g}(Vec<int> v) {{ Vec<int> items = v; public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => {(bad ? "other" : "items.AsReadOnlySpan()")}; }}",
                    bad ? "view.elision-this-violated" : "view.elision-this");
                body = $"var g = new {g}([p, 1]); Vec<int> b = [3]; var s = g.Get(b.AsReadOnlySpan()); b.Add(9); return s[0];";
                break;
            }
            case 2:
            {
                // two borrowed inputs without [LifetimeOf]: VIB007 at the declaration
                bool opt = _r.Chance(0.4);
                if (opt)
                {
                    string nd = Fresh("Nd");
                    ActType($"public sealed class {nd}(int v) {{ public int V = v; public Option<{nd}> Next = None; }}");
                    string pick = ActHelper("PickO", n => $"OptionRef<{nd}> {n}(OptionRef<{nd}> x, OptionRef<{nd}> y) => y;", "", "view.elision-ambiguous");
                    body = $"var n = new {nd}(p); var m = new {nd}(2); var r = {pick}(n.Next.AsRef(), m.Next.AsRef()); return r.IsSome ? 1 : 0;";
                }
                else
                {
                    string pick = ActHelper("Pick", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x, ReadOnlySpan<int> y) => x;", "", "view.elision-ambiguous");
                    body = $"Vec<int> a = [p, 2]; Vec<int> b = [3, 4]; var s = {pick}(a.AsReadOnlySpan(), b.AsReadOnlySpan()); return s[0];";
                }
                break;
            }
            case 3:
            {
                // single borrowed input: elision ties the result to it
                string head = ActHelper("Head", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) => x[..1];", "", "view.elision-single");
                bool mutate = _r.Chance(0.4);
                if (mutate)
                {
                    tags.Add("view.use-after-mutate");
                }
                body = $"Vec<int> a = [p, 2]; var s = {head}(a.AsReadOnlySpan()); {(mutate ? "a.Add(1); " : "")}return s[0] + {(mutate ? "1" : "a.Count")};";
                break;
            }
            case 5:
            {
                // release-7: a collection expression assigned to a by-value view parameter gets storage owned by the
                // parameter's scope (SPEC §3.7), so returning the parameter (or a slice) is VIB003 + VIB007, even where
                // C# accepts it (constant elements). The control re-slices the parameter from itself.
                int v = _r.Int(5);
                string sx = v switch
                {
                    0 => ActHelper("Sx", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) {{ x = [1, 2]; return x; }}", "", "view.param-stack-escape", "view.param-stack-scope-exit"),
                    1 => ActHelper("Sx", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x, bool f) {{ if (f) {{ x = [7, 8]; }} return x; }}", "", "view.param-stack-escape", "view.param-stack-scope-exit"),
                    2 => ActHelper("Sx", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) {{ x = [1, 2, 3]; return x[1..]; }}", "", "view.param-stack-escape", "view.param-stack-scope-exit"),
                    3 => ActHelper("Sx", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) {{ x = []; return x; }}", "", "view.param-stack-escape", "view.param-stack-scope-exit"),
                    _ => ActHelper("Sx", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) {{ x = x[1..]; return x; }}", "", "view.param-reslice-return"),
                };
                string call = v == 1 ? $"{sx}(a.AsReadOnlySpan(), p > 0)" : $"{sx}(a.AsReadOnlySpan())";
                body = v == 3
                    ? $"Vec<int> a = [p, 5, 6]; var s = {call}; return s.Length + a.Count;"
                    : $"Vec<int> a = [p, 5, 6]; var s = {call}; return s[0] + s.Length;";
                break;
            }
            default:
            {
                // a span returned from a call on a Span<T> is a reborrow of the parent (CA rule 7)
                bool pass = _r.Chance(0.4);
                string first = ActHelper("First", n => pass ? $"Span<int> {n}(Span<int> x) => x;" : $"Span<int> {n}(Span<int> x) => x[..2];");
                bool bad = _r.Chance(0.35);
                tags.Add(bad ? "view.reborrow-parent-while-live" : "view.reborrow-parent-after");
                body = bad
                    ? $"Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); Span<int> t = {first}(s); s[0] = 1; t[0] = 4; return s.Length;"
                    : $"Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); Span<int> t = {first}(s); t[0] = 4; {(pass ? "s[1] = 3; " : "")}return s.Length + v[0];";
                break;
            }
        }
        string helper = ActHelper("Vw", n => $"int {n}(int p) {{ {body} }}", "", [.. tags]);
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    // ───────────── conditional temporaries in argument position ─────────────

    private List<Stmt> PCondTemp(Inner inner)
    {
        string make = ActHelper("MakeV", n => $"Vec<int> {n}() => [1, 2, 3];");
        string len = ActHelper("Len", n => $"int {n}(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;");
        var tags = new List<string>();
        string body;
        switch (_r.Int(4))
        {
            case 0:
                tags.Add("cond.temp-arm-arg");
                body = $"Vec<int> v = [p]; bool c = p > 0; return {len}(c ? v.AsReadOnlySpan() : {make}().AsReadOnlySpan()) + {len}(!c ? v.AsReadOnlySpan() : {make}().AsReadOnlySpan());";
                break;
            case 1:
                tags.Add("cond.temp-both-arms");
                body = $"bool c = p > 0; return {len}(c ? {make}().AsReadOnlySpan() : {make}().AsReadOnlySpan()[..1]);";
                break;
            case 2:
                tags.Add("cond.temp-arm-arg");
                body = $"Vec<int> v = [p, 4]; int k = {len}(p > 2 ? {make}().AsReadOnlySpan()[1..] : v.AsReadOnlySpan()); return k + v.Count;";
                break;
            default:
                tags.Add("cond.temp-escapes-statement");
                body = $"Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? {make}().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + {len}(v.AsReadOnlySpan());";
                break;
        }
        string helper = ActHelper("Ct", n => $"int {n}(int p) {{ {body} }}", "", [.. tags]);
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    // ───────────── constant float -> long / ulong / int / uint, integer bounds ─────────────
    // (the `nint`/`nuint` forms of the Round-11 audit are cut C17; the same constant-conversion lowering runs for the
    // 64- and 32-bit integer types)

    private static readonly string[] FloatInRange = ["2.5", "-7.75", "1e6", "3e9", "-4.5e12", "0.0", "-0.0", "123.999f", "9.0e15"];
    private static readonly string[] FloatOutOfRange = ["1e19", "1e30", "-1e30", "9.3e18f", "-9.3e18", "2e19", "3.4e38f", "-1e20f", "1.8446744073709552e19"];
    private static readonly string[] FloatSpecial = ["double.PositiveInfinity", "double.NegativeInfinity", "double.NaN", "float.NaN", "float.PositiveInfinity"];

    private List<Stmt> PFloatNint(Inner inner)
    {
        var own = new List<Stmt>();
        int n = _r.Range(1, 3);
        for (int i = 0; i < n; i++)
        {
            string target = _r.Weighted((4, "long"), (2, "ulong"), (2, "int"), (1, "uint"));
            switch (_r.Int(5))
            {
                case 0:
                    own.Add(Raw($"WriteLine(unchecked(({target})({_r.Pick(FloatInRange)})));", "arith.float-int-const"));
                    break;
                case 1:
                case 2:
                    own.Add(Raw($"WriteLine(unchecked(({target})({_r.Pick(FloatOutOfRange)})));", "arith.float-int-const-oob"));
                    break;
                case 3:
                    own.Add(Raw($"WriteLine(unchecked(({target}){_r.Pick(FloatSpecial)}));", "arith.float-int-const-special"));
                    break;
                default:
                {
                    if (_r.Chance(0.5))
                    {
                        string d = Fresh("d");
                        string lit = _r.Pick([.. FloatInRange, .. FloatOutOfRange]);
                        string ft = lit.EndsWith('f') ? "float" : "double";
                        own.Add(Raw($"{ft} {d} = {lit};", "arith.float-int-runtime"));
                        own.Add(Raw($"WriteLine(unchecked(({target}){d}));", "arith.float-int-runtime"));
                    }
                    else
                    {
                        own.Add(Raw($"WriteLine({_r.Pick(["long.MinValue", "long.MaxValue", "ulong.MaxValue", "ulong.MinValue", "int.MinValue", "int.MaxValue", "uint.MaxValue"])});", "arith.int-bounds"));
                    }
                    break;
                }
            }
        }
        return WithInner(inner, own);
    }

    // ───────────── Std append to a non-seekable target (stdout/stderr pipe) ─────────────

    private bool _appendPlaced;

    private List<Stmt> PStdAppend(Inner inner)
    {
        if (!_appendPlaced)
        {
            // the first statement of the program, before any console output, so ordering cannot differ
            _appendPlaced = true;
            string target = _r.Chance(0.7) ? "/dev/stdout" : "/dev/stderr";
            string e = Fresh("e");
            if (_r.Chance(0.5))
            {
                _pendingTopStmts.Add(Raw($"if (File.AppendAllText(\"{target}\", \"appended\\n\").IsErr(out _, out var {e})) WriteLine({e}.Error.KindName()); else WriteLine(\"ok\");", "std.append-nonseekable"));
            }
            else
            {
                string f = Fresh("f");
                _pendingTopStmts.Add(Raw($"using (var {f} = File.OpenAppend(\"{target}\").Unwrap()) {{ if ({f}.WriteAll(\"appended\\n\").IsErr(out _, out var {e})) WriteLine({e}.Error.KindName()); else WriteLine(\"ok\"); }}", "std.append-nonseekable"));
            }
        }
        return inner();
    }

    // ───────────── declaration cuts C5–C9 and controls ─────────────

    private bool _ppPlaced;

    // #define must precede every token of the file (CS1032): placed after the pending top statements, before comments.
    private readonly List<Stmt> _pendingDirectives = [];

    private List<Stmt> PDeclCuts(Inner inner)
    {
        var own = new List<Stmt>();
        switch (_r.Int(7))
        {
            case 0 when !_ppPlaced:
            {
                // C5: conditional compilation (or the #region control); directives must start their own lines
                _ppPlaced = true;
                switch (_r.Int(4))
                {
                    case 0:
                        own.Add(Raw("#if DEBUG\n    WriteLine(3);\n#endif", "cut.preprocessor"));
                        break;
                    case 1:
                        own.Add(Raw("#if NET11_0_OR_GREATER\n    WriteLine(4);\n#else\n    WriteLine(2);\n#endif", "cut.preprocessor"));
                        break;
                    case 2:
                        _pendingDirectives.Add(Raw("#define VIBX", "cut.preprocessor"));
                        own.Add(Raw("#if VIBX\n    WriteLine(1);\n#endif", "cut.preprocessor"));
                        break;
                    default:
                        own.Add(Raw("#region R\n    WriteLine(5);\n#endregion", "pp.region"));
                        break;
                }
                break;
            }
            case 1:
            {
                string h = Fresh("Hidden");
                ActType($"file static class {h} {{ public static int K() => 7; }}", "cut.file-type");
                own.Add(UseInt($"{h}.K()"));
                break;
            }
            case 2:
            {
                string m = Fresh("Pm");
                if (_r.Chance(0.5))
                {
                    ActType($"static partial class {m} {{ public static partial int Twice(int x); }}", "cut.partial");
                    ActType($"static partial class {m} {{ public static partial int Twice(int x) => x * 2; }}", "cut.partial");
                    own.Add(UseInt($"{m}.Twice(4)"));
                }
                else
                {
                    ActType($"public sealed partial class {m}(int v) {{ public int V = v; }}", "cut.partial");
                    own.Add(UseInt($"new {m}(3).V"));
                }
                break;
            }
            case 3:
            {
                string b = Fresh("Gb");
                bool generic = _r.Chance(0.7);
                if (generic)
                {
                    ActType($"public sealed class {b}<T>(T t) where T : unmanaged {{ public T Val = t; public sealed class Inner(T x) {{ public T X = x; }} }}", "cut.nested-in-generic");
                    own.Add(UseInt($"new {b}<int>.Inner(2).X"));
                }
                else
                {
                    ActType($"public sealed class {b} {{ public int Val; public sealed class Inner(int x) {{ public int X = x; }} }}", "type.nested-nongeneric");
                    own.Add(UseInt($"new {b}.Inner(2).X"));
                }
                break;
            }
            case 4:
            {
                string c = Fresh("Yc");
                bool chain = _r.Chance(0.7);
                ActType(chain
                        ? $"public sealed class {c} {{ public int A; public int B; public {c}(int a) : this(a, a * 10) {{ }} public {c}(int a, int b) {{ A = a; B = b; }} }}"
                        : $"public sealed class {c} {{ public int A; public int B; public {c}(int a) {{ A = a; B = a * 10; }} public {c}(int a, int b) {{ A = a; B = b; }} }}",
                    chain ? "cut.ctor-chaining" : "type.multi-ctor");
                own.Add(UseInt($"new {c}(2).A + new {c}(1, 2).B"));
                break;
            }
            default:
            {
                // O11-11 / N15 (decided: in v0.2 a constructor may not call an instance method on `this`, whatever is
                // assigned; VIB008): before and after initialization, and a static call as the valid control
                string z = Fresh("Zc");
                int v = _r.Weighted((5, 0), (3, 1), (3, 2));
                ActType(v switch
                    {
                        0 => $"public sealed class {z} {{ public Vec<int> Items; public int K; public {z}(int k) {{ K = Peek(); Items = [k]; }} int Peek() => Items.Count; }}",
                        1 => $"public sealed class {z} {{ public Vec<int> Items; public int K; public {z}(int k) {{ Items = [k]; K = k; K += Peek(); }} int Peek() => Items.Count; }}",
                        _ => $"public sealed class {z} {{ public Vec<int> Items; public int K; public {z}(int k) {{ Items = [k]; K = Twice(k); }} static int Twice(int x) => x * 2; }}",
                    },
                    v switch { 0 => "decl.this-call-before-init", 1 => "decl.this-call-after-init", _ => "decl.ctor-static-call" });
                own.Add(UseInt($"new {z}(1).K"));
                break;
            }
        }
        return WithInner(inner, own);
    }

    /// <summary>Program-level activation choices: the generated-code marker and the generated file name (A2).</summary>
    private void ActivationProgramOptions(List<Stmt> top)
    {
        if (Profile != "activation")
        {
            return;
        }
        double roll = _r.Int(1000) / 1000.0;
        if (roll < 0.12)
        {
            top.Insert(0, Raw("// <auto-generated/>", "gen.auto-generated-header"));
        }
        else if (roll < 0.20)
        {
            top.Insert(0, Raw(Engine.GeneratedFileMarker, "gen.file-name-g-cs"));
        }
    }
}
