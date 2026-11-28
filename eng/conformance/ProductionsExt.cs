using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Vibranium.Conformance;

/// <summary>
/// Extended-profile anchors (<c>--profile extended</c>): Span slicing and ranges, Option slots through
/// <c>OptionRef</c>/<c>OptionMut</c>, <c>[Mut]</c> receivers, <c>foreach</c> over UTF-8 views, and Std text/map calls.
/// They are scheduled only by the extended profile, so every default-profile (seed, index) program is unchanged.
/// Statements are mostly <see cref="RawStmt"/> lines with their own tags: each line is one reducible unit.
/// About a quarter of the programs carry a deliberate borrow-rule near-miss (C#-valid, Vibranium-invalid): the
/// frontends must then agree on the VIB IDs.
/// </summary>
internal sealed partial class Gen
{
    private static string L(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static RawStmt Raw(string text, params string[] tags) => new(text, tags);

    /// <summary><c>acc += e;</c> when the function has an accumulator, else <c>WriteLine(e);</c>.</summary>
    private RawStmt UseInt(string e, params string[] tags) => _fn.Acc is { } a ? Raw($"{a} += {e};", tags) : Raw($"WriteLine({e});", tags);

    /// <summary>A random int expression over the visible variables, printed.</summary>
    private string IntE(int depth = 2) => Printer.Expr(Expr(Ty.Int, depth));

    /// <summary>Adds a raw helper function (static local function at top level, or public static member).</summary>
    private string RawHelper(string prefix, Func<string, string> text)
    {
        string name = Fresh(prefix);
        string t = text(name);
        if (_container == "top")
        {
            _topHelpers.Add(Raw("static " + t));
        }
        else
        {
            ClassMembers(_container).Add(Raw("public static " + t));
        }
        return name;
    }

    private static string CsString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            sb.Append(c switch { '\n' => "\\n", '\r' => "\\r", '"' => "\\\"", '\\' => "\\\\", _ => c.ToString() });
        }
        return sb.Append('"').ToString();
    }

    private bool NearMiss() => _r.Chance(0.25);

    // ───────────── Span slicing, ranges, Index ─────────────

    private List<Stmt> PSpanSlice(Inner inner)
    {
        var res = new List<Stmt>();
        string s = Fresh("s");
        int n = _r.Range(3, 6);
        string elems = string.Join(", ", Enumerable.Range(0, n).Select(_ => L(_r.Range(-5, 20))));
        string? vec = null;
        int src = _r.Int(4);
        if (src <= 1)
        {
            res.Add(Raw($"Span<int> {s} = [{elems}];", "span.from-coll"));
        }
        else
        {
            vec = Fresh("v");
            res.Add(Raw($"Vec<int> {vec} = [{elems}];", "span.from-vec"));
            string how = _r.Pick(["AsSpan()", "AsSpan()", "[..]", "[0.." + L(n) + "]", "Slice(0, " + L(n) + ")"]);
            res.Add(Raw($"Span<int> {s} = {vec}{(how.StartsWith('[') ? "" : ".")}{how};", "span.from-vec", how.StartsWith('[') ? "span.vec-range" : "span.vec-slice-call"));
        }
        Declare(s, Ty.Span(Ty.Int), VK.Local);
        int lo = _r.Range(0, n - 1);
        int hi = _r.Range(lo, n);
        bool oob = _r.Chance(0.05);
        if (oob)
        {
            hi = n + _r.Range(1, 2);
        }
        string rangeText = _r.Int(4) switch
        {
            0 => $"{L(lo)}..{L(hi)}",
            1 => $"{L(lo)}..",
            2 => $"..^{L(Math.Max(0, n - hi))}",
            _ => $"{L(lo)}..^{L(Math.Max(0, n - hi))}",
        };
        if (oob)
        {
            rangeText = $"{L(lo)}..{L(hi)}";
        }
        string t = Fresh("t");
        var rangeTags = new List<string> { "span.range" };
        if (rangeText.Contains('^'))
        {
            rangeTags.Add("span.range-from-end");
        }
        if (oob)
        {
            rangeTags.Add("span.oob-panic");
        }
        bool nm = NearMiss();
        int nmKind = nm ? _r.Int(4) : -1;
        int ops = _r.Range(2, 4);
        bool innerPlaced = false;
        for (int k = 0; k < ops; k++)
        {
            int op = _r.Int(9);
            switch (op)
            {
                case 0:
                {
                    // slice, keep it live across inner code, then use it
                    bool ro = _r.Chance(0.3);
                    res.Add(Raw($"{(ro ? "ReadOnlySpan<int>" : "Span<int>")} {t}_{k} = {s}[{rangeText}];", [.. rangeTags, ro ? "span.to-rospan" : "span.slice-local"]));
                    if (!innerPlaced)
                    {
                        res.AddRange(inner());
                        innerPlaced = true;
                    }
                    if (!ro && _r.Chance(0.5))
                    {
                        res.Add(Raw($"if ({t}_{k}.Length > 0) {t}_{k}[0] = {IntE()};", "span.write-through-slice"));
                    }
                    res.Add(_r.Chance(0.5)
                        ? Raw($"foreach (var {t}_e{k} in {t}_{k}) {(_fn.Acc is { } acc0 ? acc0 + " += " + t + "_e" + L(k) + ";" : "WriteLine(" + t + "_e" + L(k) + ");")}", "span.foreach")
                        : UseInt($"{t}_{k}.Length", "span.length"));
                    break;
                }
                case 1:
                {
                    int a = _r.Range(0, n - 1);
                    string call = _r.Chance(0.5) ? $"{s}.Slice({L(a)})" : $"{s}.Slice({L(a)}, {L(_r.Range(0, n - a))})";
                    res.Add(Raw($"ReadOnlySpan<int> {t}_r{k} = {call};", "span.slice-call", "span.to-rospan"));
                    res.Add(UseInt($"{t}_r{k}.Length + ({t}_r{k}.IsEmpty ? 0 : {t}_r{k}[^1])", "span.index-from-end"));
                    break;
                }
                case 2:
                    res.Add(UseInt($"{s}[^1] + {s}[^{L(_r.Range(1, n))}]", "span.index-from-end"));
                    break;
                case 3:
                {
                    int i = _r.Chance(0.04) ? n : _r.Range(0, n - 1);
                    res.Add(Raw($"{s}[{L(i)}] {_r.Pick(["=", "+="])} {IntE()};", i >= n ? "span.index-oob-panic" : "span.write"));
                    res.Add(UseInt($"{s}[{L(Math.Min(i, n - 1))}]", "span.read"));
                    break;
                }
                case 4:
                {
                    string sum = RawHelper("SumRo", name => $"int {name}(ReadOnlySpan<int> q) {{ int z = 0; foreach (var e in q) z += e; return z; }}");
                    res.Add(UseInt($"{sum}({(_r.Chance(0.5) ? s : s + "[" + rangeText + "]")})", "span.arg-rospan"));
                    break;
                }
                case 5:
                {
                    string bump = RawHelper("Bump", name => $"void {name}(Span<int> q, int d) {{ for (int i = 0; i < q.Length; i++) q[i] += d; }}");
                    res.Add(Raw($"{bump}({(_r.Chance(0.5) ? s : s + "[" + rangeText + "]")}, {L(_r.Range(1, 4))});", "span.arg-span"));
                    res.Add(UseInt($"{s}[0]", "span.read"));
                    break;
                }
                case 6:
                    res.Add(Raw($"foreach (ref var {t}_e{k} in {s}) {t}_e{k} += {L(_r.Range(1, 3))};", "span.foreach-ref"));
                    break;
                case 7 when _r.Chance(0.35):
                {
                    // two disjoint slices of one span in one call: same root provenance, VIB002 under SPEC §3.3
                    string pair = RawHelper("Pair", name => $"int {name}(Span<int> a, Span<int> b) {{ if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }}");
                    int mid = _r.Range(0, n);
                    res.Add(UseInt($"{pair}({s}[..{L(mid)}], {s}[{L(mid)}..])", "span.two-slices-one-call"));
                    break;
                }
                default:
                {
                    res.Add(Raw($"ReadOnlySpan<int> {t}_ro{k} = {s};", "span.to-rospan"));
                    res.Add(UseInt($"{t}_ro{k}[0] + {t}_ro{k}.Length", "span.read"));
                    res.Add(Raw($"{s}[0] = {L(_r.Range(0, 9))};", "span.write-after-shared"));
                    break;
                }
            }
        }
        if (!innerPlaced)
        {
            res.AddRange(inner());
        }
        switch (nmKind)
        {
            case 0:
            {
                string pair = RawHelper("Over", name => $"int {name}(Span<int> a, Span<int> b) => a.Length + b.Length;");
                res.Add(UseInt(_r.Chance(0.5) ? $"{pair}({s}, {s})" : $"{pair}({s}[0..2], {s}[1..3])", "span.nm-overlap"));
                break;
            }
            case 1:
                res.Add(Raw($"Span<int> {t}_w = {s}[1..];", "span.slice-local"));
                res.Add(Raw($"{s}[0] = 1;", "span.nm-parent-while-live"));
                res.Add(UseInt($"{t}_w[0]", "span.nm-parent-while-live"));
                break;
            case 2:
                res.Add(Raw($"var {t}_m = {s};", "span.nm-moved"));
                res.Add(UseInt($"{s}.Length + {t}_m.Length", "span.nm-moved"));
                break;
            case 3 when vec is not null:
                res.Add(Raw($"{vec}.Add(1);", "span.nm-vec-grow"));
                res.Add(UseInt($"{s}[0]", "span.nm-vec-grow"));
                break;
            case 3:
                res.Add(Raw($"Span<int> {t}_w = {s};", "span.nm-moved"));
                res.Add(UseInt($"{s}[0]", "span.nm-moved"));
                break;
        }
        if (vec is not null)
        {
            res.Add(UseInt($"{vec}.Count + {vec}[0]", "span.vec-after-borrow"));
        }
        return res;
    }

    // ───────────── Option slots: OptionRef / OptionMut / Take / Replace ─────────────

    private List<Stmt> POptionSlot(Inner inner)
    {
        var res = new List<Stmt>();
        string cell = Fresh("Cell");
        _types.Add(new ClassDecl(cell, "sealed", "int v", null,
            [Raw("public int V = v;"), Raw($"public Option<{cell}> Next = None;")]));
        string o = Fresh("o");
        bool some = _r.Chance(0.8);
        res.Add(Raw($"Option<{cell}> {o} = {(some ? $"Some(new {cell}({IntE(1)}))" : "None")};", "optslot.local"));
        int ops = _r.Range(2, 4);
        bool innerPlaced = false;
        for (int k = 0; k < ops; k++)
        {
            string b = Fresh("c");
            switch (_r.Int(10))
            {
                case 0:
                    res.Add(Raw($"if ({o}.AsRef() is Some<{cell}>(var {b})) {UseInt($"{b}.V").Text}", "optref.asref-pattern"));
                    break;
                case 1:
                    res.Add(Raw($"if ({o}.AsMut() is Some<{cell}>(var {b})) {b}.V += {L(_r.Range(1, 5))};", "optmut.pattern-write"));
                    break;
                case 2:
                {
                    string mm = Fresh("mm");
                    res.Add(Raw($"OptionMut<{cell}> {mm} = {o}.AsMut();", "optmut.local"));
                    if (!innerPlaced && _r.Chance(0.5))
                    {
                        res.AddRange(inner());
                        innerPlaced = true;
                    }
                    res.Add(_r.Int(3) switch
                    {
                        0 => Raw($"{mm}.Set(Some(new {cell}({L(_r.Range(0, 9))})));", "optmut.set"),
                        1 => Raw($"var {b} = {mm}.Replace({(_r.Chance(0.5) ? "None" : $"Some(new {cell}(7))")});", "optmut.replace"),
                        _ => Raw($"var {b} = {mm}.Take();", "optmut.take"),
                    });
                    break;
                }
                case 3:
                    res.Add(Raw($"var {b} = {o}.Take();", "optslot.take"));
                    res.Add(Raw($"WriteLine({b}.IsSome);", "optslot.take"));
                    break;
                case 4:
                    res.Add(Raw($"var {b} = {o}.Replace(Some(new {cell}({L(_r.Range(0, 9))})));", "optslot.replace"));
                    res.Add(Raw($"if ({b}.AsRef() is Some<{cell}>(var {b}_p)) WriteLine({b}_p.V);", "optslot.replace"));
                    break;
                case 5:
                {
                    string peek = RawHelper("Peek", name => $"int {name}(OptionRef<{cell}> r) => r is Some<{cell}>(var c) ? c.V : -1;");
                    res.Add(UseInt($"{peek}({o}.AsRef())", "optref.param"));
                    break;
                }
                case 6:
                {
                    string bump = RawHelper("BumpO", name => $"void {name}(OptionMut<{cell}> m, int d) {{ if (m is Some<{cell}>(var c)) c.V += d; }}");
                    res.Add(Raw($"{bump}({o}.AsMut(), {L(_r.Range(1, 5))});", "optmut.param"));
                    break;
                }
                case 7:
                    res.Add(Raw($"if ({o}.AsMut() is Some<{cell}>(var {b})) {b}.Next = Some(new {cell}({L(_r.Range(0, 9))}));", "optmut.chain-set"));
                    res.Add(Raw($"if ({o}.AsRef() is Some<{cell}>(var {b}_a) && {b}_a.Next.AsRef() is Some<{cell}>(var {b}_n)) WriteLine({b}_n.V);", "optref.chain"));
                    break;
                case 8:
                    res.Add(UseInt($"{o}.AsRef() switch {{ Some<{cell}>(var {b}) => {b}.V, _ => -1 }}", "optref.switch"));
                    break;
                default:
                    res.Add(Raw($"WriteLine({o}.IsSome);", "optslot.issome"));
                    break;
            }
        }
        if (!innerPlaced)
        {
            res.AddRange(inner());
        }
        if (NearMiss())
        {
            string x = Fresh("x");
            switch (_r.Int(5))
            {
                case 0:
                    res.Add(Raw($"var {x} = {o}.AsMut();", "optmut.local"));
                    res.Add(Raw($"WriteLine({o}.IsSome);", "optmut.nm-alias"));
                    res.Add(Raw($"{x}.Set(None);", "optmut.nm-alias"));
                    break;
                case 1:
                    res.Add(Raw($"if ({o}.AsRef() is Some<{cell}>(var {x})) {x}.V = 1;", "optref.nm-write"));
                    break;
                case 2:
                {
                    string keep = RawHelper("Keep", name => $"int {name}({cell} c) => c.V;");
                    res.Add(Raw($"if ({o}.AsMut() is Some<{cell}>(var {x})) WriteLine({keep}({x}));", "optmut.nm-move"));
                    break;
                }
                case 3:
                    res.Add(Raw($"if ({o} is Some<{cell}>(var {x})) {x}.V = 2;", "optslot.nm-place-write"));
                    break;
                default:
                {
                    string two = RawHelper("Two", name => $"int {name}(OptionMut<{cell}> a, OptionMut<{cell}> b) => a.IsSome ? 1 : 0;");
                    res.Add(UseInt($"{two}({o}.AsMut(), {o}.AsMut())", "optmut.nm-overlap"));
                    break;
                }
            }
        }
        res.Add(Raw($"if ({o}.AsRef() is Some<{cell}>(var {o}_f)) WriteLine({o}_f.V); else WriteLine(-1);", "optref.asref-pattern"));
        return res;
    }

    // ───────────── [Mut] receivers ─────────────

    private List<Stmt> PMutReceiver(Inner inner)
    {
        var res = new List<Stmt>();
        string cls = Fresh("Acc");
        var members = new List<Node>
        {
            Raw("public int Total = seed;"),
            Raw("public Vec<int> Items = [];"),
            Raw("[Mut] public void Add(int x) { Total += x; Items.Add(x); }"),
            Raw("public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }"),
        };
        if (_r.Chance(0.6))
        {
            members.Add(Raw("[Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }", "mut.calls-mut"));
        }
        if (_r.Chance(0.5))
        {
            members.Add(Raw("[Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }", "mut.drain"));
        }
        bool nm = NearMiss();
        int nmKind = nm ? _r.Int(5) : -1;
        if (nmKind == 0)
        {
            members.Add(Raw("public int Bad() { Add(1); return Total; }", "mut.nm-from-shared"));
        }
        _types.Add(new ClassDecl(cls, "sealed", "int seed", null, members));
        bool hasTwice = members.Any(m => m is RawStmt { Text: var tx } && tx.Contains("AddTwice", StringComparison.Ordinal));
        bool hasDrain = members.Any(m => m is RawStmt { Text: var tx } && tx.Contains("Drain", StringComparison.Ordinal));
        string a = Fresh("a");
        res.Add(Raw($"var {a} = new {cls}({IntE(1)});", "mut.local"));
        int ops = _r.Range(2, 4);
        bool innerPlaced = false;
        for (int k = 0; k < ops; k++)
        {
            switch (_r.Int(9))
            {
                case 0:
                    res.Add(Raw($"{a}.Add({IntE()});", "mut.local-call"));
                    break;
                case 1:
                    res.Add(Raw($"{a}.Add({a}.Total);", "mut.two-phase-read"));
                    break;
                case 2 when hasTwice:
                    res.Add(UseInt($"{a}.AddTwice({L(_r.Range(1, 5))})", "mut.calls-mut"));
                    break;
                case 3:
                    res.Add(UseInt($"{a}.Sum()", "mut.shared-call"));
                    break;
                case 4:
                {
                    string feed = RawHelper("Feed", name => $"void {name}(ref {cls} x, int d) {{ x.Add(d); }}");
                    res.Add(Raw($"{feed}(ref {a}, {L(_r.Range(1, 5))});", "mut.ref-param"));
                    break;
                }
                case 5:
                {
                    string look = RawHelper("Look", name => $"int {name}(in {cls} x) => x.Sum() + x.Total;");
                    res.Add(UseInt($"{look}(in {a})", "mut.in-param-shared"));
                    break;
                }
                case 6:
                    res.Add(Raw($"foreach (var {a}_e{k} in {a}.Items) WriteLine({a}_e{k});", "mut.field-foreach"));
                    break;
                case 7 when hasDrain:
                    res.Add(UseInt($"{a}.Drain()", "mut.drain"));
                    break;
                default:
                {
                    string ctr = Fresh("Ctr");
                    _types.Add(new StructDecl(ctr, "", null, [Raw("public int N;"), Raw("public void Inc() { N += 1; }"), Raw("public readonly int Get() => N;")]));
                    string c = Fresh("q");
                    res.Add(Raw($"var {c} = new {ctr}();", "mut.struct-local"));
                    res.Add(Raw($"{c}.Inc();", "mut.struct-method"));
                    res.Add(Raw($"{c}.Inc();", "mut.struct-method"));
                    res.Add(UseInt($"{c}.Get()", "mut.struct-readonly-method"));
                    break;
                }
            }
            if (!innerPlaced && _r.Chance(0.4))
            {
                res.AddRange(inner());
                innerPlaced = true;
            }
        }
        if (!innerPlaced)
        {
            res.AddRange(inner());
        }
        switch (nmKind)
        {
            case 1:
            {
                string look = RawHelper("Poke", name => $"int {name}(in {cls} x) {{ x.Add(1); return x.Total; }}");
                res.Add(UseInt($"{look}(in {a})", "mut.nm-in-param"));
                break;
            }
            case 2 when hasDrain:
                res.Add(Raw($"{a}.Add({a}.Drain());", "mut.nm-two-phase"));
                break;
            case 3:
                res.Add(Raw($"ReadOnlySpan<int> {a}_r = {a}.Items.AsReadOnlySpan();", "mut.nm-borrow-live"));
                res.Add(Raw($"{a}.Add(1);", "mut.nm-borrow-live"));
                res.Add(UseInt($"{a}_r.Length", "mut.nm-borrow-live"));
                break;
            case 4:
                res.Add(Raw($"foreach (var {a}_g in {a}.Items) {a}.Add({a}_g);", "mut.nm-foreach-mutate"));
                break;
            case 2:
                res.Add(Raw($"var {a}_h = {a};", "mut.nm-moved"));
                res.Add(Raw($"{a}.Add(1);", "mut.nm-moved"));
                break;
        }
        if (_r.Chance(0.3))
        {
            string hold = Fresh("Hold");
            _types.Add(new ClassDecl(hold, "sealed", $"{cls} inner", null, [Raw($"public {cls} In = inner;"), Raw("[Mut] public void Push(int x) { In.Add(x); }")]));
            string h = Fresh("h");
            res.Add(Raw($"var {h} = new {hold}(new {cls}({L(_r.Range(0, 5))}));", "mut.nested-field"));
            res.Add(Raw($"{h}.Push({L(_r.Range(1, 9))});", "mut.nested-field"));
            res.Add(UseInt($"{h}.In.Total + {h}.In.Sum()", "mut.nested-field"));
        }
        res.Add(UseInt($"{a}.Total + {a}.Items.Count", "mut.local"));
        return res;
    }

    // ───────────── foreach over UTF-8 views ─────────────

    private static readonly string[] Utf8Texts = ["a\nb", "a\r\nb\r\n", "\n", "", "x\ry", "one\n\ntwo", "tail\r", "\r\n\r\n", "a\n\r\nb", "abc", " 12\n-3 \nx", "k=v\n\nk2=v2\r"];

    private List<Stmt> PUtf8View(Inner inner)
    {
        var res = new List<Stmt>();
        string text = _r.Pick(Utf8Texts);
        string l = Fresh("l");
        string src;
        string? owner = null;
        string srcTag;
        switch (_r.Int(4))
        {
            case 0:
                src = CsString(text) + ".Lines()";
                srcTag = "utf8.string-lines";
                break;
            case 1:
            {
                string t = Fresh("u");
                res.Add(Raw($"ReadOnlySpan<byte> {t} = {CsString(text)}u8;", "utf8.span-local"));
                src = t + ".Lines()";
                srcTag = "utf8.span-lines";
                break;
            }
            case 2:
            {
                owner = Fresh("sb");
                res.Add(Raw($"var {owner} = new StringBuf();", "utf8.stringbuf"));
                res.Add(Raw($"{owner}.Append({CsString(text)});", "utf8.stringbuf"));
                if (_r.Chance(0.5))
                {
                    res.Add(Raw($"{owner}.AppendLine({CsString(_r.Pick(Utf8Texts))});", "utf8.stringbuf"));
                }
                if (_r.Chance(0.4))
                {
                    res.Add(Raw($"{owner}.Append({IntE(1)});", "utf8.stringbuf"));
                }
                src = owner + ".Lines()";
                srcTag = "utf8.buf-lines";
                break;
            }
            default:
            {
                owner = Fresh("vb");
                string bytes = string.Join(", ", text.Select(c => c switch { '\n' => "10", '\r' => "13", _ => "(byte)'" + c + "'" }));
                res.Add(Raw($"Vec<byte> {owner} = [{bytes}];", "utf8.vec-bytes"));
                src = owner + ".AsReadOnlySpan().Lines()";
                srcTag = "utf8.vec-lines";
                break;
            }
        }
        bool escape = _r.Chance(0.3);
        string last = Fresh("last");
        if (escape)
        {
            res.Add(Raw($"scoped ReadOnlySpan<byte> {last} = \"\"u8;", "utf8.slice-escapes-loop"));
        }
        var body = new List<string>();
        int bodyOps = _r.Range(1, 3);
        var bodyTags = new List<string> { srcTag };
        for (int k = 0; k < bodyOps; k++)
        {
            switch (_r.Int(7))
            {
                case 0:
                    body.Add($"WriteLine({l});");
                    break;
                case 1:
                    body.Add(_fn.Acc is { } acc ? $"{acc} += {l}.Length;" : $"WriteLine({l}.Length);");
                    break;
                case 2:
                    body.Add($"WriteLine({l}.StartsWith({CsString(_r.Pick(["a", "k", "", "x"]))}));");
                    bodyTags.Add("utf8.line-ops");
                    break;
                case 3:
                    body.Add($"WriteLine({l}.IndexOf((byte)'{_r.Pick(["b", "=", "2", " "])}'));");
                    bodyTags.Add("utf8.line-ops");
                    break;
                case 4:
                    body.Add($"WriteLine({l}.Trim());");
                    bodyTags.Add("utf8.line-ops");
                    break;
                case 5:
                {
                    string pv = Fresh("pv");
                    body.Add($"if (int.TryParse({l}.Trim(), out var {pv})) WriteLine({pv});");
                    bodyTags.Add("utf8.line-tryparse");
                    break;
                }
                default:
                    body.Add($"WriteLine({l}.SequenceEqual({CsString(_r.Pick(["a", "b", "two", ""]))}));");
                    bodyTags.Add("utf8.line-ops");
                    break;
            }
        }
        if (escape)
        {
            body.Add($"{last} = {l};");
        }
        bool nm = NearMiss();
        if (nm && owner is not null && owner.StartsWith("sb", StringComparison.Ordinal) && _r.Chance(0.5))
        {
            body.Add($"{owner}.Append(\"z\");");
            bodyTags.Add("utf8.nm-mutate-owner");
            nm = false;
        }
        bool innerInLoop = _r.Chance(0.4);
        List<Stmt> innerStmts;
        if (innerInLoop)
        {
            Push(isLoop: true);
            innerStmts = inner();
            Pop();
        }
        else
        {
            innerStmts = inner();
        }
        if (innerInLoop)
        {
            // the loop body is a block: raw lines plus the nested anchor's statements
            var items = new List<Stmt>(body.Select(b => (Stmt)Raw(b, [.. bodyTags])));
            items.AddRange(innerStmts);
            res.Add(Raw($"foreach (var {l} in {src})", [.. bodyTags, "utf8.foreach"]));
            res.Add(new Block(items));
        }
        else
        {
            res.Add(Raw($"foreach (var {l} in {src}) {{ {string.Join(" ", body)} }}", [.. bodyTags, "utf8.foreach"]));
            res.AddRange(innerStmts);
        }
        if (escape)
        {
            res.Add(Raw($"WriteLine({last});", "utf8.slice-escapes-loop"));
        }
        if (nm)
        {
            if (owner is not null && owner.StartsWith("sb", StringComparison.Ordinal))
            {
                res.Add(Raw($"ReadOnlySpan<byte> {owner}_k = {owner}.AsUtf8();", "utf8.nm-slice-after-clear"));
                res.Add(Raw($"{owner}.Clear();", "utf8.nm-slice-after-clear"));
                res.Add(Raw($"WriteLine({owner}_k);", "utf8.nm-slice-after-clear"));
            }
            else
            {
                res.Add(Raw($"var {l}_lines = {CsString(text)}.Lines();", "utf8.nm-view-local"));
                res.Add(Raw($"foreach (var {l}_z in {l}_lines) WriteLine({l}_z);", "utf8.nm-view-local"));
            }
        }
        return res;
    }

    // ───────────── Std text and maps ─────────────

    private static readonly string[] ParseInputs = ["42", "-7", "+3", "", " 1", "2147483647", "2147483648", "-2147483648", "0x10", "007", "-0", "1 ", "9223372036854775807", "-9223372036854775809"];

    private List<Stmt> PStdText(Inner inner)
    {
        var res = new List<Stmt>();
        int kind = _r.Int(4);
        bool nm = NearMiss();
        switch (kind)
        {
            case 0:
            {
                string m = Fresh("m");
                res.Add(Raw($"var {m} = new IntMap<int>({L(_r.Range(0, 8))});", "std.intmap"));
                int sets = _r.Range(1, 4);
                for (int k = 0; k < sets; k++)
                {
                    res.Add(Raw($"{(_r.Chance(0.5) ? "_ = " : "")}{m}.Set({L(_r.Range(-3, 9))}, {IntE(1)});", "std.intmap"));
                }
                if (_r.Chance(0.3))
                {
                    res.Add(Raw($"for (int {m}_i = 0; {m}_i < {L(_r.Range(10, 40))}; {m}_i++) {m}.Set({m}_i * {L(_r.Range(2, 9))}, {m}_i);", "std.intmap-rehash"));
                }
                res.AddRange(inner());
                string x = Fresh("x");
                res.Add(_r.Chance(0.5)
                    ? UseInt($"{m}.Get({L(_r.Range(-3, 9))}).UnwrapOr(-1)", "std.intmap")
                    : Raw($"if ({m}.Get({L(_r.Range(-3, 9))}) is Some<int>(var {x})) WriteLine({x}); else WriteLine(-1);", "std.intmap"));
                if (_r.Chance(0.4))
                {
                    res.Add(Raw($"_ = {m}.Remove({L(_r.Range(-3, 9))});", "std.intmap-remove"));
                }
                res.Add(Raw($"WriteLine({m}.Count);", "std.intmap"));
                res.Add(Raw($"WriteLine({m}.Contains({L(_r.Range(-3, 9))}));", "std.intmap"));
                if (nm)
                {
                    res.Add(Raw($"var {m}_m2 = {m};", "std.nm-map-moved"));
                    res.Add(Raw($"_ = {m}.Set(1, 1);", "std.nm-map-moved"));
                }
                break;
            }
            case 1:
            {
                string m = Fresh("sm");
                res.Add(Raw($"var {m} = new StrMap<int>({L(_r.Range(0, 8))});", "std.strmap"));
                string[] keys = ["a", "bb", "", "key", "a\n"];
                int sets = _r.Range(1, 4);
                for (int k = 0; k < sets; k++)
                {
                    string key = CsString(_r.Pick(keys)) + (_r.Chance(0.4) ? "u8" : "");
                    res.Add(Raw($"_ = {m}.Set({key}, {IntE(1)});", "std.strmap"));
                }
                res.AddRange(inner());
                res.Add(UseInt($"{m}.Get({CsString(_r.Pick(keys))}).UnwrapOr(-1)", "std.strmap"));
                res.Add(Raw($"WriteLine({m}.Count);", "std.strmap"));
                if (nm)
                {
                    res.Add(Raw($"ReadOnlySpan<byte> {m}_k = {m}.KeyAt(0);", "std.nm-map-key-borrow"));
                    res.Add(Raw($"_ = {m}.Set(\"z\", 9);", "std.nm-map-key-borrow"));
                    res.Add(Raw($"WriteLine({m}_k);", "std.nm-map-key-borrow"));
                }
                break;
            }
            case 2:
            {
                string b = Fresh("sb");
                res.Add(Raw($"var {b} = new StringBuf({L(_r.Range(0, 4))});", "std.stringbuf"));
                int ops = _r.Range(2, 5);
                for (int k = 0; k < ops; k++)
                {
                    res.Add(_r.Int(9) switch
                    {
                        0 => Raw($"{b}.Append({L(_r.Pick([0, -1, 7, -2147483648, 2147483647, 100]))});", "std.stringbuf-int"),
                        1 => Raw($"{b}.AppendLong({_r.Pick(["0L", "-9223372036854775808", "9223372036854775807L", "1L << 40", "-5L"])});", "std.stringbuf-long"),
                        2 => Raw($"{b}.Append({(_r.Chance(0.5) ? "true" : "false")});", "std.stringbuf-bool"),
                        3 => Raw($"{b}.AppendHex({_r.Pick(["255", "-1", "0", "4096", "0x7fffffffffffffffL"])}, {L(_r.Range(0, 6))});", "std.stringbuf-hex"),
                        4 => Raw($"{b}.AppendRune({_r.Pick(["0x41", "0xE9", "0x263A", "0x1F600", "0x7F", "0x80", "0x7FF", "0x800", "0xFFFF", "0x10000"])});", "std.stringbuf-rune"),
                        5 => Raw($"{b} += {CsString(_r.Pick(["x", "", "yz"]))};", "std.stringbuf-op"),
                        6 => Raw($"{b}.AppendLine({CsString(_r.Pick(["a", "", "b c"]))});", "std.stringbuf"),
                        7 => Raw($"{b}.Append({IntE(1)});", "std.stringbuf-int"),
                        _ => Raw($"{b}.Truncate({b}.Length > 0 ? {b}.Length - 1 : 0);", "std.stringbuf-truncate"),
                    });
                }
                if (_r.Chance(0.07))
                {
                    res.Add(Raw($"{b}.AppendRune({_r.Pick(["0xD800", "0x110000", "-1"])});", "std.stringbuf-rune-panic"));
                }
                res.AddRange(inner());
                res.Add(Raw($"WriteLine({b}.AsUtf8());", "std.stringbuf"));
                res.Add(Raw($"WriteLine({b}.Length);", "std.stringbuf"));
                if (nm)
                {
                    res.Add(Raw($"ReadOnlySpan<byte> {b}_k = {b}.AsUtf8();", "std.nm-buf-borrow"));
                    res.Add(Raw($"{b}.Append(1);", "std.nm-buf-borrow"));
                    res.Add(Raw($"WriteLine({b}_k);", "std.nm-buf-borrow"));
                }
                break;
            }
            default:
            {
                string t = Fresh("tx");
                res.Add(Raw($"ReadOnlySpan<byte> {t} = {CsString(_r.Pick(["  ab,cd  ", "a,b,,c", "", "xyz", " 12 ", "aaa"]))}u8;", "std.bytes"));
                int ops = _r.Range(2, 4);
                for (int k = 0; k < ops; k++)
                {
                    string pv = Fresh("pv");
                    res.Add(_r.Int(7) switch
                    {
                        0 => Raw($"WriteLine({t}.Trim());", "std.bytes-trim"),
                        1 => Raw($"WriteLine({t}.IndexOf((byte)'{_r.Pick([",", "a", "z", " "])}'));", "std.bytes-indexof"),
                        2 => Raw($"WriteLine({t}.LastIndexOf((byte)'{_r.Pick([",", "a", "z"])}'));", "std.bytes-indexof"),
                        3 => Raw($"WriteLine({t}.StartsWith({CsString(_r.Pick(["", " ", "a", "xy"]))}));", "std.bytes-prefix"),
                        4 => Raw($"WriteLine({t}.Contains({CsString(_r.Pick([",", "b,", "q"]))}));", "std.bytes-contains"),
                        5 => Raw($"if ({_r.Pick(["int", "long"])}.TryParse({CsString(_r.Pick(ParseInputs))}{(_r.Chance(0.4) ? "u8" : "")}, out var {pv})) WriteLine({pv}); else WriteLine(0);", "std.tryparse"),
                        _ => Raw($"WriteLine({t}.TrimStart().Length + {t}.TrimEnd().Length);", "std.bytes-trim"),
                    });
                }
                res.AddRange(inner());
                break;
            }
        }
        return res;
    }
}
