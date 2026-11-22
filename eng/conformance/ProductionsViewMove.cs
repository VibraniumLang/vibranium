using System.Collections.Generic;

namespace Vibranium.Conformance;

/// <summary>
/// Activation anchor <c>ViewMove</c> (release-7.1): the assignment-value cascade class that /roslyn-n16 found by hand.
/// Three families, each in its own helper function so the scenario starts at the top of its body:
/// a <c>!Copy</c> view (<c>Span&lt;int&gt;</c> or <c>OptionMut&lt;T&gt;</c>) moved by a plain move and then used through
/// the source and the destination; a view declared before a loop and reassigned in it (plain move, call result,
/// assignment value as receiver or argument) while it stays live; and the value of an assignment to a view used as a
/// receiver, an argument or a chain, then the view used again. The decided results (SPEC §3.2 assignment value,
/// the Roslyn-side decision A+): a use of the moved source is VIB001 only, a loop-reassigned view is accepted, a use
/// after an assignment value is VIB001 only; real conflicts stay VIB003/VIB001. ReadOnlySpan destinations (Copy)
/// and programs without a later use are accepted controls.
/// </summary>
internal sealed partial class Gen
{
    private List<Stmt> PViewMove(Inner inner)
    {
        var tags = new List<string>();
        string body = _r.Weighted((4, 0), (4, 1), (3, 2), (3, 3)) switch
        {
            0 => ViewMovePlain(tags),
            1 => ViewMoveLoop(tags),
            2 => ViewMoveAssignValue(tags),
            _ => ViewMoveRebind(tags),
        };
        string helper = ActHelper("Vm", n => $"int {n}(int p) {{ {body} }}", "", [.. tags]);
        return WithInner(inner, [UseInt($"{helper}({IntE(1)})")]);
    }

    /// <summary>Family 1: <c>T t = s;</c> moves a <c>!Copy</c> view; the source and the destination are used after it.</summary>
    private string ViewMovePlain(List<string> tags)
    {
        int v = _r.Weighted((3, 0), (2, 1), (2, 2), (2, 3), (1, 4), (1, 5), (2, 6));
        bool opt = v != 6 && _r.Chance(0.3);
        string decl, srcUse, dstUse, ownerRead, ownerWrite, ty;
        if (opt)
        {
            string nd = Fresh("Nd");
            ActType($"public sealed class {nd}(int v) {{ public int V = v; }}");
            ty = $"OptionMut<{nd}>";
            decl = $"Option<{nd}> o = Some(new {nd}(p)); {ty} s = o.AsMut();";
            srcUse = "(s.IsSome ? 1 : 0)";
            dstUse = "(t.IsSome ? 2 : 0)";
            ownerRead = "(o.IsSome ? 3 : 0)";
            ownerWrite = "";
            tags.Add("view.move-optionmut");
        }
        else
        {
            ty = "Span<int>";
            decl = "Vec<int> b = [p, 2, 3]; Span<int> s = b.AsSpan();";
            srcUse = _r.Pick(["s.Length", "s[0]", "s[1..].Length"]);
            dstUse = _r.Pick(["t.Length", "t[1]", "t[..2].Length"]);
            ownerRead = "b[0]";
            ownerWrite = "b[0] = 1; ";
        }
        string between(bool write) => write && ownerWrite.Length > 0 ? ownerWrite : $"n += {ownerRead}; ";
        switch (v)
        {
            case 0:
                tags.Add("view.move-use-source");
                return $"{decl} int n = 0; {ty} t = s; n += {dstUse}; n += {srcUse}; return n;";
            case 1:
                // the owner read or written after the destination's last use (C09, C16): still VIB001 only
                tags.Add("view.move-use-source");
                tags.Add("view.move-owner-between");
                return $"{decl} int n = 0; {ty} t = s; n += {dstUse}; {between(_r.Chance(0.5))}n += {srcUse}; return n;";
            case 2:
                tags.Add("view.move-use-source");
                return $"{decl} int n = 0; {ty} t = s; n += {srcUse}; n += {dstUse}; return n;";
            case 3:
                // control: only the destination is used, then the owner
                tags.Add("view.move-use-dest");
                return $"{decl} int n = 0; {ty} t = s; n += {dstUse}; n += {ownerRead}; return n;";
            case 4:
                // real conflict: the owner is read while the destination is still live (C04, C26, C27)
                tags.Add("view.move-dest-live-owner-used");
                return $"{decl} int n = 0; {ty} t = s; n += {ownerRead}; n += {dstUse}; return n;";
            case 5:
                // a move on one branch, then the owner and the source (C06): VIB001 and VIB003
                tags.Add("view.move-conditional-use-source");
                return $"{decl} int n = 0; if (p > 0) {{ {ty} t = s; n += {dstUse}; }} n += {ownerRead}; n += {srcUse}; return n;";
            default:
                // control (C05): the moved source is reassigned from another owner, then the first owner and the source
                // are used; the reassignment ends the source's old borrows
                tags.Add("view.move-reassign-source");
                return $"{decl} Vec<int> a = [4, 5]; int n = 0; {ty} t = s; n += {dstUse}; s = a.AsSpan(); n += {ownerRead}; n += {srcUse}; return n;";
        }
    }

    /// <summary>Family 2: a view declared before a loop is reassigned in it and used after the assignment.</summary>
    private string ViewMoveLoop(List<string> tags)
    {
        const string owners = "Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0;";
        string use = _r.Pick(["t.Length", "t[0]", "t[1..].Length"]);
        string after = _r.Chance(0.4) ? " n += t.Length;" : "";
        switch (_r.Weighted((3, 0), (2, 1), (3, 2), (2, 3), (1, 4), (1, 5)))
        {
            case 0:
                tags.Add("view.loop-reassign-move");
                return $"{owners} Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) {{ Span<int> s = b.AsSpan(); t = s; n += {use}; }}{after} return n;";
            case 1:
            {
                string id = ActHelper("Id", n => $"Span<int> {n}(Span<int> x) => x;");
                tags.Add("view.loop-reassign-call");
                return $"{owners} Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) {{ Span<int> s = b.AsSpan(); t = {id}(s); n += {use}; }}{after} return n;";
            }
            case 2:
            {
                // the assignment value as a receiver (C18, C20)
                string recv = _r.Pick(["(s = b.AsSpan()).Length", "(s = b.AsSpan())[0]", "(s = b.AsSpan())[1..].Length"]);
                string tail = _r.Chance(0.4) ? " s = a.AsSpan(); n += s.Length;" : "";
                tags.Add("view.loop-assign-value-receiver");
                return $"{owners} Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) {{ n += {recv};{tail} }} return n;";
            }
            case 3:
            {
                // the assignment value as an argument (C21)
                string len = ActHelper("Len", n => $"int {n}(Span<int> x) => x.Length;");
                tags.Add("view.loop-assign-value-argument");
                return $"{owners} Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) {{ n += {len}(s = b.AsSpan()); }} return n;";
            }
            case 4:
                // real conflict: the old value is used at the top of the next iteration while its owner is written (A23)
                tags.Add("view.loop-old-value-used-top");
                return $"{owners} Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) {{ b[0] = i; n += t[0]; Span<int> s = b.AsSpan(); t = s; }} return n;";
            default:
                // real conflict: the source moved by an assignment value is used at the top of the next iteration (A26)
                tags.Add("view.loop-moved-source-used");
                return $"{owners} Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) {{ b[0] = i; n += s[0]; n += (s = b.AsSpan()).Length; }} return n;";
        }
    }

    /// <summary>
    /// Family 4: a live view rebound to another owner (plain assignment, move, call result, <c>ReadOnlySpan</c>, in a loop),
    /// then its old owner mutated while the view is still used: the rebinding ends the old borrows, so this is accepted;
    /// a derived view of the old value that is still live keeps the conflict.
    /// </summary>
    private string ViewMoveRebind(List<string> tags)
    {
        const string owners = "Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0;";
        switch (_r.Weighted((3, 0), (2, 1), (2, 2), (2, 3), (2, 4), (1, 5)))
        {
            case 0:
                tags.Add("view.rebind-old-owner-mutated");
                return $"{owners} Span<int> t = a.AsSpan(); n += t[0]; t = b.AsSpan(); a.Add(7); n += t[0]; return n + a.Count;";
            case 1:
                tags.Add("view.rebind-old-owner-mutated");
                return $"{owners} Span<int> t = a.AsSpan(); n += t[0]; Span<int> s = b.AsSpan(); t = s; a.Add(7); n += t[0]; return n + a.Count;";
            case 2:
            {
                string id = ActHelper("Id", n => $"Span<int> {n}(Span<int> x) => x;");
                tags.Add("view.rebind-old-owner-mutated");
                return $"{owners} Span<int> t = a.AsSpan(); n += t[0]; t = {id}(b.AsSpan()); a.Add(7); n += t[0]; return n + a.Count;";
            }
            case 3:
                tags.Add("view.rebind-old-owner-mutated");
                return $"{owners} ReadOnlySpan<int> t = a.AsReadOnlySpan(); n += t[0]; t = b.AsReadOnlySpan(); a.Add(7); n += t[0]; return n + a.Count;";
            case 4:
                tags.Add("view.loop-rebind-old-owner-mutated");
                return $"{owners} Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) {{ n += t[0]; Span<int> s = b.AsSpan(); t = s; a.Add(i); }} return n + t.Length + a.Count;";
            default:
                // real conflict: a view derived from the old value is still live when the old owner is mutated
                tags.Add("view.rebind-old-derived-live");
                return $"{owners} Span<int> t = a.AsSpan(); Span<int> u = t[..1]; t = b.AsSpan(); a.Add(7); n += u[0] + t[0]; return n;";
        }
    }

    /// <summary>Family 3: the value of an assignment to a view used as a receiver, argument or chain, then the view.</summary>
    private string ViewMoveAssignValue(List<string> tags)
    {
        int form = _r.Weighted((3, 0), (2, 1), (2, 2), (2, 3), (2, 4), (1, 5));
        int kind = _r.Weighted((5, 0), (2, 1), (2, 2)); // 0: then use (VIB001), 1: no later use, 2: ReadOnlySpan (Copy)
        if (form == 5 && kind != 2)
        {
            string nd = Fresh("Nd");
            ActType($"public sealed class {nd}(int v) {{ public int V = v; }}");
            string decl = $"Option<{nd}> o = Some(new {nd}(p)); Option<{nd}> o2 = Some(new {nd}(2)); OptionMut<{nd}> m = o.AsMut();";
            tags.Add("view.move-optionmut");
            tags.Add(kind == 0 ? "view.assign-value-then-use" : "view.assign-value-no-later-use");
            return kind == 0
                ? $"{decl} int k = (m = o2.AsMut()).IsSome ? 1 : 0; k += m.IsSome ? 2 : 0; return k;"
                : $"{decl} int k = (m = o2.AsMut()).IsSome ? 1 : 0; k += o.IsSome ? 2 : 0; return k;";
        }
        if (form == 5)
        {
            form = 0; // OptionMut has no ReadOnlySpan-like Copy counterpart: use the receiver form
        }
        bool ro = kind == 2;
        string vt = ro ? "ReadOnlySpan<int>" : "Span<int>";
        string mk = ro ? "AsReadOnlySpan()" : "AsSpan()";
        string pre = $"Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; {vt} s = a.{mk};";
        string expr = form switch
        {
            0 => $"(s = b.{mk}).Length",
            1 => $"(s = b.{mk})[0]",
            2 => $"(s = b.{mk})[1..].Length",
            3 => $"{ActHelper("Len", n => $"int {n}({vt} x) => x.Length;")}(s = b.{mk})",
            _ => "",
        };
        string first = form == 4 ? $"{vt} t = s = b.{mk}; int k = t.Length;" : $"int k = {expr};";
        string later = kind == 1 ? " k += b[0] + a[0];" : " k += s.Length;";
        tags.Add(kind switch { 0 => "view.assign-value-then-use", 1 => "view.assign-value-no-later-use", _ => "view.assign-value-copy-view" });
        if (form == 4)
        {
            tags.Add("view.assign-value-chain");
        }
        return $"{pre} {first}{later} return k;";
    }
}
