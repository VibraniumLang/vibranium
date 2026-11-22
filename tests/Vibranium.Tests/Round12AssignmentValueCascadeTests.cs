using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// Ledger v3 assignment-value cascade (rule R3.2-assignment-value, SPEC §3.2), decision A+. Two borrow-checker rules:
// (1) a whole view moved into another view local hands its loans to the destination, and the moved-from local holds
// none until it is assigned again (any use before that is VIB001); (2) an assignment or call that stores into a whole
// local kills the destination's old value before the stored value is evaluated, unless that value reads the
// destination, so the old value's loans do not conflict with the right-hand side. Owners, projections (fields,
// elements, ref parameters) and Copy views are unchanged.
public sealed class Round12AssignmentValueCascadeTests
{
    // A view (Span<T>, OptionMut<T>) moved by a plain move or by using the value of an assignment to it, then used
    // again: exactly VIB001 on every frontend. Before the fix the Analyzer and Stage-0 also reported a knock-on
    // VIB003, because the moved-from view kept its exclusive loan. vib-self already reported exactly VIB001.
    private static readonly Dictionary<string, string> MovedView = new(StringComparer.Ordinal)
    {
        ["C01-plain-move-then-use"] = """
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(t.Length);
            WriteLine(s.Length);
            """,
        ["C03-assign-value-into-local-then-use-moved"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            Span<int> t = (s = b.AsSpan());
            WriteLine(t.Length);
            WriteLine(s.Length);
            """,
        ["C09-plain-move-write-source-then-use-moved"] = """
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(t.Length);
            b[0] = 1;
            WriteLine(s.Length);
            """,
        ["C11-assign-value-indexer-then-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan())[0];
            WriteLine(n);
            WriteLine(s.Length);
            """,
        ["C14-assign-value-slice-then-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan())[1..].Length;
            WriteLine(n);
            WriteLine(s.Length);
            """,
        ["C16-plain-move-then-use-and-read-source"] = """
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(t.Length);
            WriteLine(b[0]);
            WriteLine(s.Length);
            """,
        ["C28-optionmut-move-then-use-moved"] = """
            Option<Node> o = Some(new Node(1));
            OptionMut<Node> m = o.AsMut();
            OptionMut<Node> m2 = m;
            WriteLine(m2.IsSome);
            WriteLine(m.IsSome);
            sealed class Node(int v) { public int V = v; }
            """,
        ["v4r03-chain"] = """
            Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); Span<int> t = a.AsSpan(); t = s = b.AsSpan(); WriteLine(t.Length); WriteLine(s.Length);
            """,
        ["v4r04-index"] = """
            Vec<int> a = [1, 2]; Vec<int> b = [3, 4]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan())[0]; WriteLine(n + s[1]);
            """,
        ["A01-plain-move-use-source-before-reassign"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(s.Length);
            s = a.AsSpan();
            WriteLine(t[0] + s[0]);
            """,
        ["A16-element-write-through-moved-view"] = """
            Vec<int> v = [1, 2];
            Span<int> s = v.AsSpan();
            Span<int> t = s;
            s[0] = 7;
            WriteLine(t[0]);
            """,
        ["A36-optionmut-loop-moved-source-used-top"] = """
            Option<Node> o = Some(new Node(1));
            Option<Node> p = Some(new Node(2));
            OptionMut<Node> m = o.AsMut();
            for (int i = 0; i < 2; i++)
            {
                WriteLine(m.IsSome);
                OptionMut<Node> k2 = m;
                WriteLine(k2.IsSome);
            }
            sealed class Node(int v) { public int V = v; }
            """,
    };

    // The same for assignment values that vib-self reports only with /self-fix V4 (the value of an assignment moves a
    // !Copy view destination; vib-self in c6-pre8 accepts these or reports VIB003). v4r* are /self-fix's programs;
    // C12 and A26 are loops, where the move was rejected (VIB003) instead of reported before the fix.
    private static readonly Dictionary<string, string> MovedViewNeedsSelfV4 = new(StringComparer.Ordinal)
    {
        ["AV02-span-assign-collexpr-then-use"] = """
            Vec<int> a = [1, 2];
            Span<int> s = a.AsSpan();
            int n = (s = []).Length;
            WriteLine(n);
            WriteLine(s.Length);
            """,
        ["AV02b-span-assign-view-then-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan()).Length;
            WriteLine(n);
            WriteLine(s.Length);
            """,
        ["AV02c-span-assign-view-no-later-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan()).Length;
            WriteLine(n + s.Length);
            """,
        ["AV08-span-assign-value-arg-then-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            WriteLine(Len(s = b.AsSpan()));
            WriteLine(s.Length);

            static int Len(Span<int> x) => x.Length;
            """,
        ["C10-assign-value-write-source-then-use-moved"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan()).Length;
            b[0] = n;
            WriteLine(s.Length);
            """,
        ["C12-assign-value-loop-then-use"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                n += (s = b.AsSpan()).Length;
            }
            WriteLine(n);
            WriteLine(s.Length);
            """,
        ["v4r01-view-src"] = """
            Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan()).Length; WriteLine(n); WriteLine(s.Length);
            """,
        ["v4r02-call-arg"] = """
            Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); WriteLine(Len(s = [])); WriteLine(s.Length); static int Len(Span<int> x) => x.Length;
            """,
        ["v4r05-optionmut"] = """
            Option<Node> o = Some(new Node(1)); Option<Node> p = Some(new Node(2)); OptionMut<Node> m = o.AsMut(); bool b = (m = p.AsMut()).IsSome; WriteLine(b); WriteLine(m.IsSome); sealed class Node(int v) { public int V = v; }
            """,
        ["A02-assign-value-use-source-before-reassign"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan()).Length;
            WriteLine(s[0]);
            s = a.AsSpan();
            WriteLine(n + s[0]);
            """,
        ["A26-loop-moved-source-used-top-next-iteration"] = """
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                b[0] = i;
                n += s[0];
                n += (s = b.AsSpan()).Length;
            }
            WriteLine(n);
            """,
    };

    // Valid programs that the Analyzer and Stage-0 rejected with VIB003 before the fix (vib-self accepts them): in a
    // loop, the destination of a view assignment, an assignment value's temporary or a call result still held the
    // loans of the previous iteration's value, which the assignment overwrites. The managed run is compared with the
    // Stage-0 and vib-self native binaries at -O0 and at -O3 with ASan/LSan.
    private static readonly Dictionary<string, (string Source, string Output)> NewlyAccepted = new(StringComparer.Ordinal)
    {
        ["C17-plain-move-loop-outer-dest"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = a.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                Span<int> s = b.AsSpan();
                t = s;
                WriteLine(t.Length);
            }
            """, "3\n3\n"),
        ["C18-assign-value-loop-no-later-use"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                n += (s = b.AsSpan()).Length;
            }
            WriteLine(n);
            """, "6\n"),
        ["C20-assign-value-loop-reassigned-use"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                n += (s = b.AsSpan()).Length;
                s = a.AsSpan();
                n += s.Length;
            }
            WriteLine(n);
            """, "10\n"),
        ["C21-assign-value-call-loop-no-later-use"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = 0;
            for (int i = 0; i < 2; i++)
            {
                n += Len(s = b.AsSpan());
            }
            WriteLine(n);

            static int Len(Span<int> x) => x.Length;
            """, "6\n"),
        ["C22-call-dest-loop-outer"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = a.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                Span<int> s = b.AsSpan();
                t = Id(s);
                WriteLine(t.Length);
            }

            static Span<int> Id(Span<int> x) => x;
            """, "3\n3\n"),
        ["A30-optionmut-loop-outer-dest"] = ("""
            Option<Node> o = Some(new Node(1));
            Option<Node> p = Some(new Node(2));
            OptionMut<Node> m = o.AsMut();
            for (int i = 0; i < 2; i++)
            {
                OptionMut<Node> k2 = p.AsMut();
                m = k2;
                WriteLine(m.IsSome);
            }
            sealed class Node(int v) { public int V = v; }
            """, "True\nTrue\n"),
    };

    // Controls accepted before and after: the moved view is reassigned before its next use or not used again, a
    // ReadOnlySpan<T> or OptionRef<T> (Copy) destination stays usable, a loop-carried view is dead when its owner is
    // overwritten, stack-storage views, and a linear owner moved by an assignment value.
    private static readonly Dictionary<string, (string Source, string Output)> AcceptedControls = new(StringComparer.Ordinal)
    {
        ["C05-move-reassign-then-read-source"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(t.Length);
            s = a.AsSpan();
            WriteLine(b[0]);
            WriteLine(s.Length);
            """, "3\n3\n2\n"),
        ["C07-rospan-assign-view-then-use"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            ReadOnlySpan<int> r = a.AsSpan();
            int n = (r = b.AsSpan()).Length;
            WriteLine(n);
            WriteLine(r.Length);
            """, "3\n3\n"),
        ["C15-assign-value-accepted-no-use"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            int n = (s = b.AsSpan()).Length;
            WriteLine(n);
            WriteLine(b[0]);
            WriteLine(a[0]);
            """, "3\n3\n1\n"),
        ["C19-plain-move-loop-inner-dest"] = ("""
            Vec<int> b = [3, 4, 5];
            for (int i = 0; i < 2; i++)
            {
                Span<int> s = b.AsSpan();
                Span<int> u = s;
                WriteLine(u.Length);
            }
            """, "3\n3\n"),
        ["A11-owner-overwritten-loop-carried-view-dead"] = ("""
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                WriteLine(sp[0]);
                v = MakeVec(i);
                sp = v.AsSpan();
            }

            static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
            """, "1\n0\n"),
        ["A33-optionref-assign-value-old-owner-written"] = ("""
            Option<Node> o = Some(new Node(1));
            Option<Node> q = Some(new Node(2));
            OptionRef<Node> r = o.AsRef();
            bool x = (r = q.AsRef()).IsSome;
            o = Some(new Node(5));
            WriteLine(x);
            WriteLine(r.IsSome);
            sealed class Node(int v) { public int V = v; }
            """, "True\nTrue\n"),
        ["A34-stack-span-moved-source-reassigned"] = ("""
            Span<int> s = [1, 2, 3];
            Span<int> t = s;
            s = [4, 5];
            WriteLine(t[0] + s[0]);
            """, "5\n"),
        ["A37-linear-owner-assign-value-accepted"] = ("""
            R r = new R(1);
            Consume(r);
            using var q = (r = new R(2));
            WriteLine(q.Id);

            static void Consume(R x) { using var y = x; }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "-1\n2\n-2\n"),
    };

    // Adversarial rows that stay rejected, with the same ID set on every frontend: the moved source used before it is
    // reassigned, a value that reads its own destination with a real conflict (`s = s[1..]`, `t = Pick(t, s)`,
    // `s = Id(s)` in a loop), an owner overwritten while a view of it is live (straight-line, loop, assignment value),
    // field, element and ref-parameter destinations, an assignment on one branch only with the old value used on the
    // other, a loop whose old value is used at the top of the next iteration, linear owners, and OptionMut/OptionRef
    // cursors. A04 and A07 lose a duplicate VIB003 (the same-call overlap is VIB002 on every frontend).
    private static readonly Dictionary<string, (string Source, string[] Ids)> StaysRejected = new(StringComparer.Ordinal)
    {
        ["C02-assign-value-into-local-then-read-source"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            Span<int> t = (s = b.AsSpan());
            WriteLine(b[0]);
            WriteLine(t.Length);
            """, ["VIB003"]),
        ["C04-plain-move-then-read-source-live-dest"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            WriteLine(b[0]);
            WriteLine(t.Length);
            """, ["VIB003"]),
        ["C06-conditional-move-then-read-source"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            if (b.Count > 1)
            {
                Span<int> t = s;
                WriteLine(t.Length);
            }
            WriteLine(b[0]);
            WriteLine(s.Length);
            """, ["VIB001", "VIB003"]),
        ["C08-call-arg-span-then-read-source"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            WriteLine(Len(s));
            WriteLine(b[0]);
            WriteLine(s.Length);

            static int Len(Span<int> x) => x.Length;
            """, ["VIB003"]),
        ["C13-assign-value-dest-live-conflict"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            Span<int> t = (s = b.AsSpan());
            b[0] = 7;
            WriteLine(t[0]);
            """, ["VIB003"]),
        ["C23-optionmut-move-into-call-then-conflict"] = ("""
            Option<Node> o = Some(new Node(1));
            OptionMut<Node> m = o.AsMut();
            OptionMut<Node> m2 = Pass(m);
            WriteLine(o.IsSome);
            WriteLine(m2.IsSome);

            static OptionMut<Node> Pass(OptionMut<Node> x) => x;
            sealed class Node(int v) { public int V = v; }
            """, ["VIB003"]),
        ["C26-move-then-reborrow-source-while-dest-live"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            Span<int> t = s;
            s = b.AsSpan();
            WriteLine(s.Length);
            WriteLine(t.Length);
            """, ["VIB003"]),
        ["C27-optionmut-move-then-read-source"] = ("""
            Option<Node> o = Some(new Node(1));
            OptionMut<Node> m = o.AsMut();
            OptionMut<Node> m2 = m;
            WriteLine(o.IsSome);
            WriteLine(m2.IsSome);
            sealed class Node(int v) { public int V = v; }
            """, ["VIB003"]),
        ["C29-assign-value-loop-dest-live-conflict"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            Span<int> t = a.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                t = (s = b.AsSpan());
                b[0] = i;
                WriteLine(t[0]);
            }
            """, ["VIB003"]),
        ["A03-rhs-reads-dest-slice-then-conflict"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            WriteLine(b[0]);
            s = s[1..];
            WriteLine(s[0]);
            """, ["VIB003"]),
        ["A04-rhs-reads-dest-pick-borrows-source-again"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            s = Pick(s, b.AsSpan());
            WriteLine(s[0]);

            static Span<int> Pick(Span<int> x, Span<int> y) => x.Length > y.Length ? x : y;
            """, ["VIB002", "VIB007"]),
        ["A05-rhs-reads-dest-pick-derived"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> t = b.AsSpan();
            Span<int> s = t[1..];
            t = Pick(t, s);
            WriteLine(t[0]);

            static Span<int> Pick(Span<int> x, Span<int> y) => x.Length > y.Length ? x : y;
            """, ["VIB002", "VIB003", "VIB007"]),
        ["A06-rhs-reads-dest-id-loop-conflict"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                b[0] = i;
                s = Id(s);
            }
            WriteLine(s[0]);

            static Span<int> Id(Span<int> x) => x;
            """, ["VIB003"]),
        ["A07-rhs-reads-dest-pick-loop-conflict"] = ("""
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                s = Pick(s, b.AsSpan());
            }
            WriteLine(s[0]);

            static Span<int> Pick(Span<int> x, Span<int> y) => x.Length > y.Length ? x : y;
            """, ["VIB002", "VIB007"]),
        ["A08-owner-overwritten-view-live"] = ("""
            Vec<int> v = [1, 2];
            Vec<int> other = [3];
            Span<int> sp = v.AsSpan();
            v = other;
            WriteLine(sp[0]);
            """, ["VIB003"]),
        ["A09-owner-overwritten-view-live-loop"] = ("""
            Vec<int> v = [1, 2];
            for (int i = 0; i < 2; i++)
            {
                Span<int> sp = v.AsSpan();
                v = MakeVec(i);
                WriteLine(sp[0]);
            }

            static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
            """, ["VIB003"]),
        ["A10-owner-assign-value-view-live"] = ("""
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            int n = (v = MakeVec(5)).Count;
            WriteLine(n + sp[0]);

            static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
            """, ["VIB003"]),
        ["A12-owner-overwritten-loop-carried-view-live"] = ("""
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                v = MakeVec(i);
                WriteLine(sp[0]);
                sp = v.AsSpan();
            }

            static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
            """, ["VIB003"]),
        ["A13-field-owner-overwritten-view-live"] = ("""
            H h = new H();
            Span<int> sp = h.V.AsSpan();
            h.V = [7];
            WriteLine(sp[0]);
            sealed class H { public Vec<int> V = [1, 2]; }
            """, ["VIB003"]),
        ["A14-field-dest-moved-owner-used"] = ("""
            H h = new H();
            Vec<int> x = [4, 5];
            h.V = x;
            WriteLine(x.Count);
            sealed class H { public Vec<int> V = [1, 2]; }
            """, ["VIB001"]),
        ["A15-element-owner-written-view-live"] = ("""
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            v[0] = 7;
            WriteLine(sp[0]);
            """, ["VIB003"]),
        ["A17-ref-param-dest-moved-owner-used"] = ("""
            Vec<int> a = [1, 2];
            Set(ref a, [3, 4, 5]);
            WriteLine(a.Count);

            static void Set(ref Vec<int> d, Vec<int> x)
            {
                d = x;
                WriteLine(x.Count);
            }
            """, ["VIB001"]),
        ["A18-ref-param-owner-overwritten-view-live"] = ("""
            Vec<int> a = [1, 2];
            F(ref a);
            WriteLine(a.Count);

            static void F(ref Vec<int> d)
            {
                Span<int> sp = d.AsSpan();
                d = [9];
                WriteLine(sp[0]);
            }
            """, ["VIB003"]),
        ["A19-ref-span-param-dest"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = a.AsSpan();
            Set(ref s, b.AsSpan());
            WriteLine(s.Length);

            static void Set(ref Span<int> d, Span<int> x)
            {
                d = x;
                WriteLine(x.Length);
            }
            """, ["VIB000", "VIB001"]),
        ["A20-one-branch-assign-old-value-used-other"] = ("""
            int k = 3;
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = b.AsSpan();
            Span<int> s = a.AsSpan();
            if (k > 1)
            {
                t = s;
            }
            else
            {
                b[0] = 7;
            }
            WriteLine(t[0]);
            """, ["VIB003"]),
        ["A21-one-branch-move-reassign-source-used-other"] = ("""
            int k = 3;
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> s = b.AsSpan();
            if (k > 1)
            {
                Span<int> u = s;
                WriteLine(u[0]);
                s = a.AsSpan();
            }
            else
            {
                b[0] = 7;
            }
            WriteLine(s[0]);
            """, ["VIB003"]),
        ["A22-one-branch-assign-value-old-value-used-other"] = ("""
            int k = 3;
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = b.AsSpan();
            int n = 0;
            if (k > 1)
            {
                n = (t = a.AsSpan()).Length;
                t = a.AsSpan();
            }
            else
            {
                b[0] = 7;
            }
            WriteLine(n + t[0]);
            """, ["VIB003"]),
        ["A23-loop-old-value-used-top-before-reassign"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = a.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                b[0] = i;
                WriteLine(t[0]);
                Span<int> s = b.AsSpan();
                t = s;
            }
            """, ["VIB003"]),
        ["A24-loop-assign-value-old-value-used-top"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Vec<int> c = [6];
            Span<int> s = a.AsSpan();
            Span<int> t = c.AsSpan();
            for (int i = 0; i < 2; i++)
            {
                b[0] = i;
                WriteLine(t[0]);
                t = (s = b.AsSpan());
            }
            """, ["VIB003"]),
        ["A25-loop-continue-old-value-flows"] = ("""
            Vec<int> a = [1, 2];
            Vec<int> b = [3, 4, 5];
            Span<int> t = b.AsSpan();
            for (int i = 0; i < 3; i++)
            {
                WriteLine(t[0]);
                b[0] = i;
                if (i == 0) continue;
                t = a.AsSpan();
            }
            """, ["VIB003"]),
        ["A27-linear-owner-moved-then-used"] = ("""
            R r = new R(1);
            using var q = r;
            WriteLine(r.Id);
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB001"]),
        ["A28-linear-owner-assign-value-overwrites-live"] = ("""
            R r = new R(1);
            using var q = (r = new R(2));
            WriteLine(q.Id);
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB006"]),
        ["A29-linear-owner-assign-value-then-used"] = ("""
            R r = new R(1);
            Consume(r);
            using var q = (r = new R(2));
            WriteLine(r.Id);

            static void Consume(R x) { using var y = x; }
            sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, ["VIB001"]),
        ["A31-optionmut-assign-value-dest-live-conflict"] = ("""
            Option<Node> o = Some(new Node(1));
            Option<Node> p = Some(new Node(2));
            OptionMut<Node> m = o.AsMut();
            OptionMut<Node> m2 = (m = p.AsMut());
            WriteLine(p.IsSome);
            WriteLine(m2.IsSome);
            sealed class Node(int v) { public int V = v; }
            """, ["VIB003"]),
        ["A32-optionref-copy-then-owner-written"] = ("""
            Option<Node> o = Some(new Node(1));
            OptionRef<Node> r = o.AsRef();
            OptionRef<Node> r2 = r;
            o = Some(new Node(3));
            WriteLine(r2.IsSome);
            WriteLine(r.IsSome);
            sealed class Node(int v) { public int V = v; }
            """, ["VIB003"]),
    };

    public static TheoryData<string> MovedViewNames => new(MovedView.Keys);
    public static TheoryData<string> MovedViewNeedsSelfV4Names => new(MovedViewNeedsSelfV4.Keys);
    public static TheoryData<string> NewlyAcceptedNames => new(NewlyAccepted.Keys);
    public static TheoryData<string> AcceptedControlsNames => new(AcceptedControls.Keys);
    public static TheoryData<string> StaysRejectedNames => new(StaysRejected.Keys);

    [Theory]
    [MemberData(nameof(MovedViewNames))]
    public Task MovedView_UsedAgain_IsExactlyVib001(string name) =>
        Round12RuleWitness.RejectAsync(MovedView[name], "VIB001", exact: true);

    [Theory]
    [MemberData(nameof(MovedViewNeedsSelfV4Names))]
    public Task AssignmentValue_MovedViewUsedAgain_IsExactlyVib001(string name) =>
        Round12RuleWitness.RejectAsync(MovedViewNeedsSelfV4[name], "VIB001", exact: true);

    [Theory]
    [MemberData(nameof(NewlyAcceptedNames))]
    public async Task OverwrittenDestination_DoesNotRejectValidProgram(string name)
    {
        var (source, output) = NewlyAccepted[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(AcceptedControlsNames))]
    public async Task Control_StaysAccepted(string name)
    {
        var (source, output) = AcceptedControls[name];
        await Round12RuleWitness.AcceptAsync(source, output);
    }

    [Theory]
    [MemberData(nameof(StaysRejectedNames))]
    public async Task Adversarial_StaysRejectedWithTheSameIdSet(string name)
    {
        var (source, ids) = StaysRejected[name];
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.True(ids.SequenceEqual(result.Analyzer), $"Analyzer reported [{string.Join(",", result.Analyzer)}]");
        Assert.True(ids.SequenceEqual(result.Stage0), $"Stage-0 reported [{string.Join(",", result.Stage0)}]");
        Assert.True(ids.SequenceEqual(result.Self), $"vib-self reported [{string.Join(",", result.Self)}]\n{result.SelfOutput}");
    }
}
