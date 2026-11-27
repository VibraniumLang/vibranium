using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

// N118 (Q-INSPAN): a mutable Span reached through a read-only path is a read-only view: passing it where a mutable Span
// value is taken (a by-value argument, an extension `this`, a constructor argument) or `foreach (ref …)` over it is VIB004.
// N128: a mutating call on an element of a ReadOnlySpan reached by a from-end index is VIB004, as by a plain index.
public sealed class Round12ReadOnlySpanViewTests
{
    public static IEnumerable<object[]> RejectRows()
    {
        yield return ["b118-a01-byval-writer", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { Zero(s); }
            static void Zero(Span<int> t) { t[0] = 0; }
            """];
        yield return ["b118-a02-byval-reader", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) => Sum(s);
            static int Sum(Span<int> t) => t[0] + t[1] + t[2];
            """];
        yield return ["b118-a03-byval-later-arg", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { Put(s, f ? 1 : 0, 7); }
            static void Put(Span<int> t, int i, int v) { t[i] = v; }
            """];
        yield return ["b118-a04-byval-last-after-branch", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { Put(f ? 1 : 0, s); }
            static void Put(int i, Span<int> t) { t[i] = 7; }
            """];
        yield return ["b118-a05-ext-this", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { s.Zero(); }
            public static class Ext { public static void Zero(this Span<int> t) { t[0] = 0; } }
            """];
        yield return ["b118-a06-ext-this-branch-arg", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { s.Put(f ? 1 : 0); }
            public static class Ext { public static void Put(this Span<int> t, int i) { t[i] = 7; } }
            """];
        yield return ["b118-a07-class-ctor-arg", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { var c = new C(s); WriteLine(c.N); }
            public sealed class C { public int N; public C(Span<int> t) { t[0] = 0; N = t.Length; } }
            """];
        yield return ["b118-a08-struct-ctor-arg", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { var c = new P(s); WriteLine(c.N); }
            public struct P { public int N; public P(Span<int> t) { t[0] = 0; N = t.Length; } }
            """];
        yield return ["b118-a09-instance-method-arg", """
            Vec<int> a = [1, 2, 3];
            var k = new K();
            F(k, a.AsSpan());
            WriteLine(a[0]);
            static void F(K k, in Span<int> s) { k.Zero(s); }
            public sealed class K { public void Zero(Span<int> t) { t[0] = 0; } }
            """];
        yield return ["b118-a11-in-forwarded-then-byval", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { G(in s); }
            static void G(in Span<int> p) { Zero(p); }
            static void Zero(Span<int> t) { t[0] = 0; }
            """];
        yield return ["b118-a12-struct-readonly-member-in-param", """
            Vec<int> a = [1, 2, 3];
            var q = new Q(5);
            q.Run(a.AsSpan());
            WriteLine(a[0]);
            public struct Q(int n) { public int N = n; public readonly void Run(in Span<int> s) { Zero(s); } static void Zero(Span<int> t) { t[0] = 0; } }
            """];
        yield return ["b118-a13-mut-member-in-param", """
            Vec<int> a = [1, 2, 3];
            var q = new Q(5);
            q.Run(a.AsSpan());
            WriteLine(a[0] + q.N);
            public struct Q(int n) { public int N = n; [Mut] public void Run(in Span<int> s) { N = 1; Zero(s); } static void Zero(Span<int> t) { t[0] = 0; } }
            """];
        yield return ["b118-a47-in-span-1arg-passed-by-value-nocycle", """
            Vec<int> v = [10, 20];
            Span<int> s = v.AsSpan();
            ref readonly int ro = ref s[0];
            MutIn(in s);
            WriteLine(ro);
            static void MutIn(in Span<int> t) { Set1(t); }
            static void Set1(Span<int> u) { u[0] = 99; }
            """];
        yield return ["b118-b01-foreach-ref", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { foreach (ref int x in s) x = 0; }
            """];
        yield return ["b118-b03-foreach-ref-bound-only", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) { int t = 0; foreach (ref int x in s) t += x; return t; }
            """];
        yield return ["b118-b04-foreach-ref-passed-ref", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { foreach (ref int x in s) Inc(ref x); }
            static void Inc(ref int p) { p += 1; }
            """];
        yield return ["b118-b05-foreach-ref-struct-mut", """
            Vec<P> a = [new P(1), new P(2)];
            F(a.AsSpan());
            WriteLine(a[0].X);
            static void F(in Span<P> s) { foreach (ref P x in s) x.Bump(); }
            public struct P(int x) { public int X = x; [Mut] public void Bump() { X += 1; } }
            """];
        yield return ["b118-o01-pass-in-span-by-value-read", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => Sum(s) + s[f ? 1 : 0];
            static int Sum(Span<int> t) { int k = 0; foreach (int x in t) { k += x; } return k; }
            """];
        yield return ["b118-o02-pass-in-span-by-value-writer", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { Zero(s, f ? 1 : 0); }
            static void Zero(Span<int> t, int i) { t[i] = 0; }
            """];
        yield return ["b118-x12-foreach-ref-write", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[0]);
            static void F(in Span<int> s, bool f) { foreach (ref int x in s) { x += f ? 1 : 2; } }
            """];
        yield return ["b118-y01-foreach-ref-write-plain", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[1]);
            static void F(in Span<int> s) { foreach (ref int x in s) { x = 0; } }
            """];
        yield return ["b118-y02-two-in-refs-one-span-foreach-ref-write-while-shared-ref-live", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in s));
            static int F(in Span<int> p, in Span<int> q) { ref readonly int r = ref q[0]; int before = r; foreach (ref int x in p) { x += 10; } return before * 100 + r; }
            """];
        yield return ["b118-y03-two-in-refs-one-span-copied-out-to-two-mutable-spans", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in s));
            static int F(in Span<int> p, in Span<int> q) => G(p, q);
            static int G(Span<int> x, Span<int> y) { x[0] = 10; y[0] = 20; return x[0]; }
            """];
        yield return ["b118-y04-two-mutable-views-loop-hoist", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in s));
            static int F(in Span<int> p, in Span<int> q) => G(p, q);
            static int G(Span<int> x, Span<int> y) { int sum = 0; for (int i = 0; i < x.Length; i++) { x[i] = y[0] + 1; sum += y[0]; } return sum; }
            """];
        yield return ["b118-y05-caller-shared-ref-live-callee-foreach-ref-writes", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            ref readonly int r = ref s[0];
            int before = r;
            W(in s);
            WriteLine(before * 100 + r);
            static void W(in Span<int> p) { foreach (ref int x in p) { x += 10; } }
            """];
        yield return ["b118-y08-foreach-ref-element-passed-by-ref", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[1]);
            static void F(in Span<int> s) { foreach (ref int x in s) { Bump(ref x); } }
            static void Bump(ref int x) { x += 5; }
            """];
        yield return ["b118-y09-foreach-ref-struct-element-field-and-mut-member", """
            Vec<P> a = [new P(1), new P(2)];
            F(a.AsSpan());
            WriteLine(a[1].X);
            static void F(in Span<P> s) { foreach (ref P e in s) { e.X = 5; e.Bump(4); } }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """];
        yield return ["b118-y14-two-in-refs-one-copied-out-one-read-in-place", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in s));
            static int F(in Span<int> p, in Span<int> q) => H(in q, p);
            static int H(in Span<int> x, Span<int> y) { int k = x[0]; y[0] = 9; return x[0] + k - k; }
            """];
        yield return ["b118-y16-in-span-and-in-vec-of-same-buffer", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in a));
            static int F(in Span<int> p, in Vec<int> v) { int k = v[0]; Zero(p, 0); return v[0] + 9 + k - k; }
            static void Zero(Span<int> t, int i) { t[i] = 0; }
            """];
        yield return ["b118-y17-ros-and-in-span-of-same-buffer-copied-out", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            ReadOnlySpan<int> r = s;
            WriteLine(F(in s, r));
            static int F(in Span<int> p, ReadOnlySpan<int> q) { int k = q[0]; Zero(p, 0); return q[0] + k - k; }
            static void Zero(Span<int> t, int i) { t[i] = 0; }
            """];
        yield return ["b128-r1rosloc-gh-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros[..][^1].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r1rosloc-h-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros[^1].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r1rosloc-sh-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros.Slice(0)[^1].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r2rosparam-gh-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsReadOnlySpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(ReadOnlySpan<Pt> ros) { ros[..][^1].SetX(99); }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r2rosparam-h-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsReadOnlySpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(ReadOnlySpan<Pt> ros) { ros[^1].SetX(99); }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r2rosparam-sh-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsReadOnlySpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(ReadOnlySpan<Pt> ros) { ros.Slice(0)[^1].SetX(99); }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """];
        yield return ["b128-r5rofieldros-gh-mut", """
            var h = new H([new Pt(1, 2), new Pt(3, 4)]);
            h.V.AsReadOnlySpan()[..][^1].SetX(99);
            WriteLine(h.V[0].X);
            WriteLine(h.V[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            public sealed class H(Vec<Pt> v) { public readonly Vec<Pt> V = v; }
            """];
        yield return ["b128-r5rofieldros-h-mut", """
            var h = new H([new Pt(1, 2), new Pt(3, 4)]);
            h.V.AsReadOnlySpan()[^1].SetX(99);
            WriteLine(h.V[0].X);
            WriteLine(h.V[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            public sealed class H(Vec<Pt> v) { public readonly Vec<Pt> V = v; }
            """];
        yield return ["b128-r5rofieldros-sh-mut", """
            var h = new H([new Pt(1, 2), new Pt(3, 4)]);
            h.V.AsReadOnlySpan().Slice(0)[^1].SetX(99);
            WriteLine(h.V[0].X);
            WriteLine(h.V[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            public sealed class H(Vec<Pt> v) { public readonly Vec<Pt> V = v; }
            """];
    }

    [Theory]
    [MemberData(nameof(RejectRows))]
    public Task MutableUseOfReadOnlySpanView_IsExactlyVib004(string shape, string source) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, "VIB004");

    // Exact Roslyn ID sets: rows that already had another verdict, unchanged rows, and accepted rows whose vib-self
    // differs (self is fixed elsewhere).
    public static IEnumerable<object[]> ControlRows()
    {
        yield return ["b118-a10-ref-readonly-local", """
            Vec<int> a = [1, 2, 3];
            Span<int> s0 = a.AsSpan();
            ref readonly Span<int> r = ref s0;
            Zero(r);
            WriteLine(a[0]);
            static void Zero(Span<int> t) { t[0] = 0; }
            """, "VIB000,VIB004"];
        yield return ["b118-a14-cond-arg", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [4, 5, 6];
            F(a.AsSpan(), b.AsSpan(), a.Count > 0);
            WriteLine(a[0] + b[0]);
            static void F(in Span<int> s, in Span<int> t, bool f) { Zero(f ? s : t); }
            static void Zero(Span<int> t) { t[0] = 0; }
            """, "VIB005"];
        yield return ["b118-a15-move-call", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { Zero(Move(s)); }
            static void Zero(Span<int> t) { t[0] = 0; }
            """, "VIB005"];
        yield return ["b118-b02-foreach-ref-refro-local", """
            Vec<int> a = [1, 2, 3];
            Span<int> s0 = a.AsSpan();
            ref readonly Span<int> r = ref s0;
            foreach (ref int x in r) x = 0;
            WriteLine(a[0]);
            """, "VIB000,VIB004"];
        yield return ["b118-b06-foreach-ref-in-vec-control", """
            Vec<int> a = [1, 2, 3];
            F(in a);
            WriteLine(a[0]);
            static void F(in Vec<int> v) { foreach (ref int x in v) x = 0; }
            """, "VIB004"];
        yield return ["b118-e06-in-span-assign-existing-local", """
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            WriteLine(F(sp));
            static int F(in Span<int> s)
            {
                Vec<int> w = [5];
                Span<int> t = w.AsSpan();
                t = s;
                t[0] = 9;
                return t[0];
            }
            """, "VIB005"];
        yield return ["b118-e07-in-span-is-var-pattern", """
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            WriteLine(F(sp));
            static int F(in Span<int> s)
            {
                if (s is var t) { t[0] = 9; return t[0]; }
                return 0;
            }
            """, "VIB005"];
        yield return ["b118-e08-in-span-switch-var", """
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            WriteLine(F(sp));
            static int F(in Span<int> s) => s switch { var t => G(t) };
            static int G(Span<int> t) { t[0] = 9; return t[0]; }
            """, "VIB005"];
        yield return ["b118-e10-ref-readonly-param-byval", """
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            WriteLine(F(in sp));
            static int F(ref readonly Span<int> s) => G(s);
            static int G(Span<int> t) { t[0] = 9; return t[0]; }
            """, "VIB000"];
        yield return ["b118-f01-in-optionmut-byval", """
            Option<Box> o = Some(new Box(1));
            var m = o.AsMut();
            F(in m);
            WriteLine(o.IsSome);
            static void F(in OptionMut<Box> m) { G(m); }
            static void G(OptionMut<Box> x) { x.Take(); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["b118-k01-copy-local", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { Span<int> t = s; t[0] = 0; }
            """, "VIB005"];
        yield return ["b118-k02-return", """
            Vec<int> a = [1, 2, 3];
            Span<int> t = F(a.AsSpan());
            t[0] = 0;
            WriteLine(a[0]);
            static Span<int> F(in Span<int> s) => s;
            """, "VIB005"];
        yield return ["b118-o03-copy-in-span-to-local-then-write", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { Span<int> t = s; t[f ? 1 : 0] = 9; }
            """, "VIB005"];
        yield return ["b118-o04-conditional-of-two-in-spans-write", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [4, 5, 6];
            F(a.AsSpan(), b.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, in Span<int> t, bool f) { (f ? s : t)[1] = 5; }
            """, "VIB005"];
        yield return ["b118-o05-return-in-span-by-value", """
            Vec<int> a = [1, 2, 3];
            Span<int> t = G(a.AsSpan());
            t[1] = 9;
            WriteLine(a[1]);
            static Span<int> G(in Span<int> s) => s;
            """, "VIB005"];
        yield return ["b118-x11-slice-zero-plain", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(in Span<int> s) { Span<int> t = s.Slice(0); t[0] = 9; }
            """, "VIB004"];
        yield return ["b118-y06-one-in-span-passed-twice-by-value", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> p) => G(p, p);
            static int G(Span<int> x, Span<int> y) { x[0] = 10; y[0] = 20; return x[0]; }
            """, "VIB002,VIB004"];
        yield return ["b118-y10-in-span-by-value-to-writer-while-shared-ref-live", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) { ref readonly int r = ref s[0]; int before = r; Zero(s, 0); return before * 100 + r; }
            static void Zero(Span<int> t, int i) { t[i] = 0; }
            """, "VIB003,VIB004"];
        yield return ["b118-y12-in-and-by-value-of-same-span-at-top-level", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(H(in s, s));
            static int H(in Span<int> x, Span<int> y) { y[0] = 9; return x[0]; }
            """, "VIB002"];
        yield return ["b118-y13-in-param-passed-by-in-and-by-value", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> p) => H(in p, p);
            static int H(in Span<int> x, Span<int> y) { y[0] = 9; return x[0]; }
            """, "VIB002,VIB004"];
        yield return ["b118-z02-ref-struct-span-field-readonly-member", """
            Vec<int> a = [1, 2, 3];
            var w = new Wsp(a.AsSpan());
            WriteLine(w.Get(a.Count > 0));
            public ref struct Wsp { public Span<int> S; public Wsp(Span<int> s) { S = s; } public readonly int Get(bool f) => S[f ? 1 : 0]; }
            """, "VIB000,VIB004"];
        yield return ["b128-r1rosloc-g-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros[1..][0].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r1rosloc-i-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros[0].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r1rosloc-s-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            ReadOnlySpan<Pt> ros = v.AsReadOnlySpan();
            ros.Slice(1)[0].SetX(99);
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r3inspan-gh-ref", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsSpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(in Span<Pt> ros) { Inc(ref ros[..][^1].X); }
            static void Inc(ref int p) { p += 1; }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r3inspan-h-fld", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsSpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(in Span<Pt> ros) { ros[^1].X = 99; }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r3inspan-h-mut", """
            Vec<Pt> v = [new Pt(1, 2), new Pt(3, 4)];
            Run(v.AsSpan());
            WriteLine(v[0].X);
            WriteLine(v[1].X);
            static void Run(in Span<Pt> ros) { ros[^1].SetX(99); }
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            """, "VIB004"];
        yield return ["b128-r5rofieldros-i-mut", """
            var h = new H([new Pt(1, 2), new Pt(3, 4)]);
            h.V.AsReadOnlySpan()[0].SetX(99);
            WriteLine(h.V[0].X);
            WriteLine(h.V[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            public sealed class H(Vec<Pt> v) { public readonly Vec<Pt> V = v; }
            """, "VIB004"];
        yield return ["b128-r6rofieldspan-h-mut", """
            var h = new H([new Pt(1, 2), new Pt(3, 4)]);
            h.V.AsSpan()[^1].SetX(99);
            WriteLine(h.V[0].X);
            WriteLine(h.V[1].X);
            public struct Pt(int x, int y)
            {
                public int X = x;
                public int Y = y;
                [Mut] public void SetX(int val) { X = val; }
            }
            public sealed class H(Vec<Pt> v) { public readonly Vec<Pt> V = v; }
            """, "VIB004"];
    }

    [Theory]
    [MemberData(nameof(ControlRows))]
    public Task Controls_HaveExactRoslynIds(string shape, string source, string ids) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, ids);

    public static IEnumerable<object[]> AcceptedRows()
    {
        yield return ["b118-e01-in-byte-span-bcl-reads", """
            Vec<byte> v = [97, 98, 99, 1, 2];
            Span<byte> sp = v.AsSpan();
            WriteLine(F(sp, true));
            static int F(in Span<byte> s, bool f)
            {
                int n = 0;
                if (s.SequenceEqual("abc"u8)) n += 1;
                if (s.StartsWith("ab"u8)) n += 2;
                if (s.EndsWith("ab"u8)) n += 4;
                if (s.Contains((byte)2)) n += 8;
                n += s.IndexOf((byte)99) * 16;
                n += s.LastIndexOf((byte)1) * 100;
                return n;
            }
            """, "342\n"];
        yield return ["b118-e02-in-byte-span-bcl-reads-branch-arg", """
            Vec<byte> v = [97, 98, 99, 1, 2];
            Span<byte> sp = v.AsSpan();
            WriteLine(F(sp, true));
            WriteLine(F(sp, false));
            static int F(in Span<byte> s, bool f)
            {
                int n = s.IndexOf(f ? (byte)99 : (byte)1);
                if (s.Contains(f ? (byte)2 : (byte)7)) n += 10;
                if (s.StartsWith(f ? "ab"u8 : "x"u8)) n += 100;
                return n;
            }
            """, "112\n3\n"];
        yield return ["b118-e05-in-span-isempty", """
            Vec<int> v = [1, 2];
            Span<int> sp = v.AsSpan();
            WriteLine(F(sp));
            static int F(in Span<int> s) => s.IsEmpty ? 0 : s.Length + s[0];
            """, "3\n"];
        yield return ["b118-e09-in-span-to-ros-param-branch", """
            Vec<int> v = [1, 2, 3];
            Span<int> sp = v.AsSpan();
            WriteLine(F(sp, true));
            static int F(in Span<int> s, bool f) => H(s, f ? 1 : 2);
            static int H(ReadOnlySpan<int> r, int i) => r[i];
            """, "2\n"];
        yield return ["b118-k03-to-ros-param", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) => Sum(s);
            static int Sum(ReadOnlySpan<int> t) => t[0] + t[1] + t[2];
            """, "6\n"];
        yield return ["b118-k04-to-ros-local", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) { ReadOnlySpan<int> r = s; return r[f ? 1 : 0] + r[1..].Length; }
            """, "4\n"];
        yield return ["b118-k05-pass-in", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) => G(in s, in s);
            static int G(in Span<int> p, in Span<int> q) => p[0] + q[1];
            """, "3\n"];
        yield return ["b118-k06-reads-length", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 1));
            static int F(in Span<int> s, bool f) { int t = s.Length; foreach (int x in s) t += x; foreach (ref readonly int y in s) t += y; return t + s[f ? 2 : 0] + s[^1]; }
            """, "21\n"];
        yield return ["b118-k07-byval-span-param", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[0]);
            static void F(Span<int> s) { Zero(s); foreach (ref int x in s) x += 1; }
            static void Zero(Span<int> t) { t[0] = 0; }
            """, "1\n"];
        yield return ["b118-k08-local-span-byval", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            Zero(s);
            Zero(s);
            WriteLine(a[0]);
            static void Zero(Span<int> t) { t[0] = 0; }
            """, "0\n"];
        yield return ["b118-k09-local-span-byval-after-branch", """
            Vec<int> a = [1, 2, 3];
            bool f = a.Count > 0;
            Span<int> s = a.AsSpan();
            Put(f ? 1 : 0, s);
            Put2(s, f ? 2 : 0);
            WriteLine(a[1] + a[2]);
            static void Put(int i, Span<int> t) { t[i] = 7; }
            static void Put2(Span<int> t, int i) { t[i] = 8; }
            """, "15\n"];
        yield return ["b118-k10-byval-param-span-after-branch", """
            Vec<int> a = [1, 2, 3];
            F(a.Count > 0, a.AsSpan());
            WriteLine(a[1] + a[2]);
            static void F(bool f, Span<int> s) { Put(f ? 1 : 0, s); Put2(s, f ? 2 : 0); }
            static void Put(int i, Span<int> t) { t[i] = 7; }
            static void Put2(Span<int> t, int i) { t[i] = 8; }
            """, "15\n"];
        yield return ["b118-k11-in-span-to-ros-arg-after-branch", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => Sum(f ? 1 : 0, s);
            static int Sum(int k, ReadOnlySpan<int> t) => t[k] + t[2];
            """, "5\n"];
        yield return ["b118-y11-byvalue-span-controls-foreach-ref", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            foreach (ref int x in s) { x += 5; }
            F(s);
            WriteLine(a[0]);
            WriteLine(a[1]);
            WriteLine(a[2]);
            static void F(Span<int> t) { foreach (ref int x in t) { x += 5; } }
            """, "11\n12\n13\n"];
        yield return ["b118-y15-two-in-refs-one-span-reads-only", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            WriteLine(F(in s, in s, a.Count > 0));
            static int F(in Span<int> p, in Span<int> q, bool f) => p[f ? 0 : 1] + q[f ? 0 : 2];
            """, "2\n"];
        yield return ["bw-w01-ros-param-view-then-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { var v = s[1..]; s = v; return s[0]; }
            """, "2\n"];
        yield return ["bw-w02-ros-param-self-slice-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { s = s[1..]; return s[0]; }
            """, "2\n"];
        yield return ["bw-w03-ros-param-slice-method-view-then-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { ReadOnlySpan<int> v = s.Slice(1); s = v; return s[0]; }
            """, "2\n"];
        yield return ["bw-w04-ros-param-view-used-after-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { var v = s[1..]; s = v; return s[0] * 10 + v[1]; }
            """, "23\n"];
        yield return ["bw-w05-ros-param-loop-view-rebind", """
            Vec<int> a = [1, 2, 3, 4];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { int t = 0; while (s.Length > 0) { t = t * 10 + s[0]; var v = s[1..]; s = v; } return t; }
            """, "1234\n"];
        yield return ["bw-w06-ros-local-view-then-rebind", """
            Vec<int> a = [1, 2, 3];
            ReadOnlySpan<int> s = a.AsReadOnlySpan();
            var v = s[1..];
            s = v;
            WriteLine(s[0]);
            """, "2\n"];
        yield return ["bw-w07-span-param-view-then-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(Span<int> s) { var v = s[1..]; s = v; s[0] = 7; return s[0]; }
            """, "7\n"];
        yield return ["bw-w08-ros-param-cond-view-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan(), a.Count > 0));
            static int F(ReadOnlySpan<int> s, bool f) { var v = f ? s[1..] : s; s = v; return s[0]; }
            """, "2\n"];
        yield return ["bw-w09-ros-param-view-index-rebind", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { var v = s[1..]; s = v[..1]; return s[0]; }
            """, "2\n"];
        yield return ["bw-w10-ros-param-two-views-swap", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan()));
            static int F(ReadOnlySpan<int> s) { var v = s[1..]; var u = s[..1]; s = v; return s[0] * 10 + u[0]; }
            """, "21\n"];
    }

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public async Task SoundRows_AcceptWithNativeParity(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }
}
