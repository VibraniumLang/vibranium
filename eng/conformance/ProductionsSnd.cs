using System;
using System.Collections.Generic;
using System.Linq;

namespace Vibranium.Conformance;

/// <summary>
/// Soundness profile (<c>--profile soundness</c>): every loan producer (span and read-only span views, slices, a view
/// returned through a helper, a ref local to an element or field, <c>OptionRef</c>/<c>OptionMut</c>, a view returned by
/// a receiver method) crossed with every invalidator of its owner (move, consuming call, growth, clear, resize,
/// reassignment, the class owner's scope exit, <c>Take</c>/<c>Replace</c>, a <c>[Mut]</c> call, moving a field out) and every placement of
/// the later use (straight line, before the invalidation, re-acquired, a branch, disjoint branches, a loop, a loop that
/// re-acquires, a switch section, nested call arguments in both orders, after a loop). The oracle is expectation-free:
/// a program every frontend accepts must run ASan/LSan-clean at O0 and O3 with the managed output, so an accepted
/// unsafe program is a root whatever SPEC says about the shape. Each scenario lives in its own helper function.
/// From release-7 each scenario also carries its static SPEC expectation (<c>snd.expect-*</c>, vib-reject rows), so a
/// shared deviation (every frontend accepts a conflicting shape) is a <c>spec-accepted</c> bucket even with
/// <c>--no-exec</c>.
/// </summary>
internal sealed partial class Gen
{
    /// <summary>Soundness anchors: one per loan-producer family.</summary>
    public static readonly Anchor[] SoundnessAnchors =
        [Anchor.SndSpan, Anchor.SndSlice, Anchor.SndView, Anchor.SndRefLocal, Anchor.SndOptRef, Anchor.SndOptMut, Anchor.SndRecv];

    /// <summary>The soundness profile pairs its anchors with the default ones (banned constructs excluded).</summary>
    public static readonly Anchor[] SoundnessProfileAnchors = [.. DefaultAnchors.Where(a => a != Anchor.Banned), .. SoundnessAnchors];

    private string? _sndHolder;
    private string? _sndCell;

    private string SndHolder()
    {
        if (_sndHolder is null)
        {
            _sndHolder = Fresh("Hold");
            ActType($"public sealed class {_sndHolder}(int p) {{ public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }}");
        }
        return _sndHolder;
    }

    private string SndCell()
    {
        if (_sndCell is null)
        {
            _sndCell = Fresh("Cell");
            ActType($"public sealed class {_sndCell}(int v) {{ public int V = v; }}");
        }
        return _sndCell;
    }

    /// <summary>One loan scenario: the loan, the owner's invalidation and the later use, in a placement.</summary>
    private List<Stmt> PSnd(Anchor a, Inner inner)
    {
        var tags = new List<string>();
        // ── producer and owner ──
        string producer = a switch
        {
            Anchor.SndSpan => _r.Pick(["span", "ros"]),
            Anchor.SndSlice => _r.Pick(["slice-range", "slice-method", "slice-ro", "vec-range"]),
            Anchor.SndView => "view-helper",
            Anchor.SndRefLocal => _r.Pick(["ref-elem", "ref-elem", "ref-field"]),
            Anchor.SndOptRef => "optref",
            Anchor.SndOptMut => "optmut",
            _ => "recv-view",
        };
        string owner = producer switch { "ref-field" or "recv-view" => "holder", "optref" or "optmut" => "option", _ => "vec" };
        tags.Add("snd.producer-" + producer);
        string cell = owner == "option" ? SndCell() : "";
        string hold = owner == "holder" ? SndHolder() : "";
        string ownerDecl = owner switch
        {
            "vec" => "Vec<int> v = [p, 2, 3];",
            "holder" => $"var h = new {hold}(p);",
            _ => $"Option<{cell}> o = Some(new {cell}(p));",
        };
        string head = producer == "view-helper" ? ActHelper("Head", n => $"ReadOnlySpan<int> {n}(ReadOnlySpan<int> x) => x[..1];") : "";
        // the loan as a declaration and as a re-acquiring assignment
        (string loanType, string acquire) = producer switch
        {
            "span" => ("Span<int>", "v.AsSpan()"),
            "ros" => ("ReadOnlySpan<int>", "v.AsReadOnlySpan()"),
            "slice-range" => ("Span<int>", "v.AsSpan()[0..2]"),
            "slice-method" => ("Span<int>", "v.Slice(0, 2)"),
            "slice-ro" => ("ReadOnlySpan<int>", "v.ReadOnlySlice(1, 2)"),
            "vec-range" => ("Span<int>", "v[0..2]"),
            "view-helper" => ("ReadOnlySpan<int>", $"{head}(v.AsReadOnlySpan())"),
            "ref-elem" => ("ref int", "ref v[0]"),
            "ref-field" => ("ref int", "ref h.K"),
            "optref" => ("var", "o.AsRef()"),
            "optmut" => ("var", "o.AsMut()"),
            _ => ("ReadOnlySpan<int>", "h.View()"),
        };
        bool isRef = loanType == "ref int";
        string decl = $"{loanType} s = {acquire};";
        string reacquire = isRef ? $"s = ref {acquire[4..]};" : $"s = {acquire};";
        // ── the use ──
        bool writable = producer is "span" or "slice-range" or "slice-method" or "vec-range" or "ref-elem" or "ref-field" or "optmut";
        bool write = writable && _r.Chance(0.4);
        tags.Add(write ? "snd.use-write" : "snd.use-read");
        string readExpr = producer switch
        {
            "ref-elem" or "ref-field" => "s",
            "optref" or "optmut" => _r.Chance(0.5) ? "(s.IsSome ? 1 : 0)" : $"(s is Some<{cell}>(var c) ? c.V : 0)",
            _ => _r.Chance(0.7) ? "s[0]" : "s.Length",
        };
        string use = write
            ? producer switch
            {
                "ref-elem" or "ref-field" => "s = p + 1; acc += s;",
                "optmut" => $"if (s is Some<{cell}>(var c)) {{ c.V += 1; acc += c.V; }}",
                _ => "s[0] = p; acc += s[0];",
            }
            : $"acc += {readExpr};";
        string useExpr = readExpr;
        // ── the invalidation of the owner ──
        string inval = owner switch
        {
            // a view of a struct owner (`Vec`, `Option`) cannot leave the owner's block in C# (CS8168/CS8347), so the
            // scope-exit invalidation uses the class owner, whose views C# lets escape
            "vec" => _r.Pick(["move", "sink", "sink-explicit", "growth", "clear", "resize", "reassign"]),
            "holder" => _r.Pick(["mut-call", "move", "reassign", "field-move", "items-growth", "scope-exit"]),
            _ => _r.Pick(["take", "reassign", "move", "replace"]),
        };
        tags.Add("snd.inval-" + inval);
        string growBody = inval switch { "growth" => "x.Add(k);", "clear" => "x.Clear();", "resize" => "x.Resize(8, k);", "reassign" => "x = [k];", _ => "" };
        bool nestable = owner == "vec" && (growBody.Length > 0 || inval is "move" or "sink" or "sink-explicit");
        // ── placement ──
        var places = new List<string> { "after", "before", "reacquire", "branch", "branch-disjoint", "loop", "loop-reacquire", "switch", "after-loop" };
        if (nestable)
        {
            places.Add("nested-use-first");
            places.Add("nested-use-last");
        }
        string place = inval == "scope-exit" ? "scope-exit" : _r.Pick(places);
        tags.Add("snd.place-" + place);
        bool nested = place.StartsWith("nested", StringComparison.Ordinal);
        // helpers are added only when used (an unused local function is CS8321)
        string sink = inval is "sink" or "sink-explicit" || nested && inval == "move" ? ActHelper("Sink", n => $"int {n}(Vec<int> x) => x.Count;") : "";
        string I = (owner, inval) switch
        {
            ("vec", "move") => "var w = v; acc += w.Count;",
            ("vec", "sink") => $"acc += {sink}(v);",
            ("vec", "sink-explicit") => $"acc += {sink}(Move(v));",
            ("vec", "growth") => "v.Add(p);",
            ("vec", "clear") => "v.Clear();",
            ("vec", "resize") => "v.Resize(8, p);",
            ("vec", "reassign") => "v = [p];",
            ("holder", "mut-call") => "h.Push(p);",
            ("holder", "move") => "var w = h; acc += w.K;",
            ("holder", "reassign") => $"h = new {hold}(1);",
            ("holder", "field-move") => "var it = h.Items; acc += it.Count;",
            ("holder", "items-growth") => "h.Items.Add(p);",
            ("option", "take") => "var t = o.Take(); acc += t.IsSome ? 1 : 0;",
            ("option", "reassign") => "o = None;",
            ("option", "move") => "var w = o; acc += w.IsSome ? 1 : 0;",
            ("option", "replace") => $"var old = o.Replace(Some(new {cell}(2))); acc += old.IsSome ? 1 : 0;",
            _ => "",
        };
        // expression form of the invalidation for nested call arguments (vec owners)
        string invalExpr = !nested ? "0" : inval switch
        {
            "sink-explicit" => $"{sink}(Move(v))",
            "move" or "sink" => $"{sink}(v)",
            _ => $"{ActHelper("Grow", n => $"int {n}(ref Vec<int> x, int k) {{ {growBody} return x.Count; }}")}(ref v, p)",
        };
        string pair = nested ? ActHelper("Pair", n => $"int {n}(int a, int b) => a * 10 + b;") : "";
        string body = place switch
        {
            "after" => $"{ownerDecl} {decl} {I} {use}",
            "before" => $"{ownerDecl} {decl} {use} {I}",
            "reacquire" => $"{ownerDecl} {decl} {I} {reacquire} {use}",
            "branch" => $"{ownerDecl} {decl} if (p > 1) {{ {I} }} {use}",
            "branch-disjoint" => $"{ownerDecl} {decl} if (p > 1) {{ {use} }} else {{ {I} }}",
            "loop" => $"{ownerDecl} {decl} for (int i = 0; i < 2; i++) {{ {use} if (i == 0) {{ {I} }} }}",
            "loop-reacquire" => $"{ownerDecl} for (int i = 0; i < 2; i++) {{ {decl} {use} {I} }}",
            "switch" => $"{ownerDecl} {decl} switch (p % 3) {{ case 0: {{ {I} break; }} default: break; }} {use}",
            "after-loop" => $"{ownerDecl} {decl} for (int i = 0; i < p % 3; i++) {{ {I} }} {use}",
            "nested-use-first" => $"{ownerDecl} {decl} acc += {pair}({useExpr}, {invalExpr});",
            "nested-use-last" => $"{ownerDecl} {decl} acc += {pair}({invalExpr}, {useExpr});",
            // scope exit: the owner lives in an inner block, the view (or the re-pointed ref local) outlives it
            _ => isRef
                ? $"int d = 0; ref int s = ref d; {{ {ownerDecl} {reacquire} }} {use}"
                : $"{loanType} s = []; {{ {ownerDecl} {reacquire} }} {use}",
        };
        // static SPEC expectation (§3), so that a deviation every frontend shares is visible without running anything:
        // a later use keeps the loan live across a conflicting invalidation (VIB003); re-acquiring from an owner that
        // was moved (VIB001); moving a field out of a class (VIB005, whatever the placement). The loan on `h.K` is
        // disjoint from `h.Items`, so moving or growing `Items` does not conflict with it.
        bool conflicts = !(producer == "ref-field" && inval is "field-move" or "items-growth");
        bool liveAcross = place is "after" or "branch" or "loop" or "switch" or "after-loop" or "nested-use-last" or "scope-exit";
        bool consumes = inval is "move" or "sink" or "sink-explicit";
        if (conflicts && liveAcross)
        {
            tags.Add("snd.expect-conflict");
        }
        else if (consumes && place is "reacquire" or "loop-reacquire")
        {
            tags.Add("snd.expect-use-after-move");
        }
        if (inval == "field-move")
        {
            tags.Add("snd.expect-field-move");
        }
        string helper = ActHelper("Snd", n => $"int {n}(int p) {{ int acc = 0; {body} return acc; }}", "", [.. tags]);
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }
}
