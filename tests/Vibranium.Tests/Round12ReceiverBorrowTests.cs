using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12ReceiverBorrowTests
{
    public static IEnumerable<object[]> ReceiverUsedByArgument()
    {
        yield return ["h032-option-unwrapor-arg-sets-some", """
            Option<int> o = None;
            WriteLine(o.UnwrapOr(Fill(ref o)));
            static int Fill(ref Option<int> x) { x = Some(42); return 5; }
            """, "VIB003"];
        yield return ["h045-struct-field-option-unwrapor", """
            var h = new H(Some(1));
            WriteLine(h.O.UnwrapOr(ClearH(ref h)));
            static int ClearH(ref H p) { p.O = None; return 5; }
            struct H(Option<int> o) { public Option<int> O = o; }
            """, "VIB003"];
        yield return ["h046-class-field-option-unwrapor", """
            var c = new C(Some(1));
            WriteLine(c.O.UnwrapOr(c.Clear()));
            class C(Option<int> o) { public Option<int> O = o; [Mut] public int Clear() { O = None; return 5; } }
            """, "VIB003"];
        yield return ["h047-vec-elem-option-unwrapor", """
            Vec<Option<int>> v = [Some(1), Some(2)];
            WriteLine(v[0].UnwrapOr(ClearV(ref v)));
            static int ClearV(ref Vec<Option<int>> p) { p[0] = None; return 5; }
            """, "VIB003"];
        yield return ["h048-option-unwrapor-nested-arg", """
            Option<int> o = Some(1);
            WriteLine(Pair(o.UnwrapOr(Clear(ref o)), 0));
            Option<int> p = Some(2);
            int k = p.UnwrapOr(0) + p.UnwrapOr(Clear(ref p));
            WriteLine(k);
            static int Clear(ref Option<int> x) { x = None; return 5; }
            static int Pair(int a, int b) => a * 1000 + b;
            """, "VIB003"];
        yield return ["h052-unwrapor-assignexpr-arg", """
            Option<int> o = Some(1);
            WriteLine(o.UnwrapOr((o = None).UnwrapOr(5)));
            """, "VIB003"];
        yield return ["r04b-result-unwrapor-assignexpr", """
            Result<int, int> r = Ok(1);
            WriteLine(r.UnwrapOr((r = Err(2)).UnwrapOr(5)));
            """, "VIB003"];
        yield return ["r28-result-unwrapor-class-field-mut", """
            var c = new C(Ok(1));
            WriteLine(c.R.UnwrapOr(c.Fail()));
            class C(Result<int, int> r) { public Result<int, int> R = r; [Mut] public int Fail() { R = Err(2); return 5; } }
            """, "VIB003"];
        yield return ["r29-option-unwrapor-ref-local-alias", """
            Option<int> o = Some(1);
            ref Option<int> a = ref o;
            WriteLine(a.UnwrapOr(Clear(ref o)));
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["r31-option-unwrapor-this-field-class", """
            var c = new C(Some(1));
            WriteLine(c.M());
            class C(Option<int> o)
            {
                public Option<int> O = o;
                [Mut] public int M() => O.UnwrapOr(Reset());
                [Mut] int Reset() { O = None; return 5; }
            }
            """, "VIB003"];
        yield return ["r32-option-unwrapor-this-field-struct", """
            var s = new S(Some(1));
            WriteLine(s.M());
            struct S(Option<int> o)
            {
                public Option<int> O = o;
                public int M() => O.UnwrapOr(Reset());
                int Reset() { O = None; return 5; }
            }
            """, "VIB003"];
        yield return ["r33-option-unwrapor-ref-return-recv", """
            var h = new H(Some(1));
            WriteLine(Slot(ref h).UnwrapOr(Clear(ref h)));
            static ref Option<int> Slot(ref H p) => ref p.O;
            static int Clear(ref H p) { p.O = None; return 5; }
            struct H(Option<int> o) { public Option<int> O = o; }
            """, "VIB003"];
        yield return ["r43-parenthesized-place-receiver", """
            Option<int> o = Some(1);
            WriteLine((o).UnwrapOr(Clear(ref o)));
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["f14-a04-span-elem-option-unwrapor", """
            Vec<Option<int>> v = [Some(1), Some(2)];
            Span<Option<int>> s = v.AsSpan();
            WriteLine(s[0].UnwrapOr(ClearV(ref v)));
            static int ClearV(ref Vec<Option<int>> p) { p[0] = None; return 5; }
            """, "VIB003"];
        yield return ["f14-a05-refassign-recv-writes-through-alias", """
            Option<int> o = Some(1);
            Option<int> d = None;
            ref Option<int> x = ref d;
            WriteLine((x = ref o).UnwrapOr(Clear(ref x)));
            static int Clear(ref Option<int> r) { r = None; return 5; }
            """, "VIB003"];
        yield return ["p04-span-elem-write-in-index", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            int k = s[s[0] = 2];
            WriteLine(k);
            WriteLine(a[0]);
            """, "VIB003"];
        yield return ["p05-span-slice-elem-write-in-arg", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            Span<int> t = s.Slice(s[0] = 1);
            WriteLine(t[0]);
            WriteLine(a[0]);
            """, "VIB003"];
        yield return ["p08-class-field-recv-written-by-call", """
            var c = new C();
            WriteLine(c.F.UnwrapOr(Reset(c)));
            static int Reset(C c) { c.F = None; return 5; }
            public sealed class C { public Option<int> F = Some(1); }
            """, "VIB003"];
        yield return ["p09-span-elem-write-in-index-ref", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            ref int e = ref s[s[0] = 2];
            e = 9;
            WriteLine(a[2]);
            WriteLine(a[0]);
            """, "VIB003"];
        yield return ["f14-a01-noncopy-unwrapor-ref-helper", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(Clear(ref o)).V);
            static Box Clear(ref Option<Box> x) { x = None; return new Box(5); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["f14-a02-noncopy-expect-ref-helper", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.Expect(Msg(ref o)).V);
            static string Msg(ref Option<Box> x) { x = None; return "was cleared"; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["f14-a03-result-noncopy-unwrapor-ref-helper", """
            Result<Box, int> r = Ok(new Box(1));
            WriteLine(r.UnwrapOr(Fail(ref r)).V);
            static Box Fail(ref Result<Box, int> x) { x = Err(9); return new Box(5); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["p07-cond-arg-noncopy-recv", """
            Option<Box> o = Some(new Box(1));
            bool f = Id(true);
            WriteLine(o.UnwrapOr(f ? Clear(ref o) : new Box(3)).V);
            static bool Id(bool b) => b;
            static Box Clear(ref Option<Box> x) { x = None; return new Box(5); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["d1-r1-noncopy-arg-reads", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(Make(o.IsSome)).V);
            Result<Box, int> r = Err(4);
            WriteLine(r.UnwrapOr(Make(r.IsOk)).V);
            Option<Box> e = Some(new Box(3));
            WriteLine(e.Expect(Text(e.IsSome)).V);
            static Box Make(bool b) => new Box(b ? 7 : 8);
            static string Text(bool b) => b ? "a" : "b";
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["d1-r3-noncopy-arg-reads-param", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(Peek(in o)).V);
            static Box Peek(in Option<Box> x) => new Box(x.IsSome ? 7 : 8);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["p49-linear-expect-writer", """
            Option<Res> o = Some(new Res(1));
            using var r = o.Expect(Msg(ref o));
            WriteLine(r.Id);
            static string Msg(ref Option<Res> x) { x = None; return "was cleared"; }
            public sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB001,VIB006"];
        yield return ["p47-generic-noncopy-recv-writer", """
            WriteLine(Pick(Some(1), 5));
            static T Pick<T>(Option<T> o, T d) => o.UnwrapOr(Clr(ref o, d));
            static T Clr<T>(ref Option<T> x, T d) { x = None; return d; }
            """, "VIB001,VIB003"];
        yield return ["f14-a11-ros-index-write-through-copy", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [7, 8, 9];
            ReadOnlySpan<int> r = a.AsReadOnlySpan();
            int k = r[Swap(ref r, b.AsReadOnlySpan())];
            WriteLine(k);
            static int Swap(ref ReadOnlySpan<int> x, ReadOnlySpan<int> y) { x = y; return 1; }
            """, "VIB000,VIB003,VIB004"];
    }

    [Theory]
    [MemberData(nameof(ReceiverUsedByArgument))]
    [MemberData(nameof(ReceiverArgumentsD1w))]
    public Task ArgumentThatUsesReceiver_HasExactRoslynIds(string shape, string source, string ids) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, ids);

    public static IEnumerable<object[]> ReceiverControls()
    {
        yield return ["f14-k01-option-args-read-receiver", """
            Option<int> o = Some(1);
            WriteLine(o.UnwrapOr(o.IsSome ? 2 : 3));
            WriteLine(o.UnwrapOr(o.UnwrapOr(4)));
            Result<int, int> r = Err(2);
            WriteLine(r.UnwrapOr(r.IsOk ? 5 : 6));
            WriteLine(o.Expect(o.IsSome ? "a" : "b"));
            WriteLine(o.UnwrapOr(Peek(in o)));
            Option<int> n = None;
            WriteLine(n.UnwrapOr(n.UnwrapOr(9) + 1));
            static int Peek(in Option<int> x) => x.IsSome ? 7 : 8;
            """, "1\n1\n6\n1\n1\n10\n", 0, ""];
        yield return ["f14-k02-span-args-read-receiver", """
            Vec<int> a = [1, 2, 3, 4];
            Span<int> s = a.AsSpan();
            int x1 = s[s.Length - 1];
            int x2 = s[^(s.Length - 2)];
            Span<int> t = s.Slice(s.Length - 2);
            t[0] = 30;
            Span<int> u = s[(s.Length - 3)..];
            u[0] = 20;
            s[s.Length - 1] = 40;
            ref int e = ref s[s.Length - 2];
            e = 31;
            WriteLine(x1 * 1000 + x2);
            WriteLine(a[1] + a[2] + a[3]);
            ReadOnlySpan<int> r = a.AsReadOnlySpan();
            WriteLine(r[r.Length - 1] + r.Slice(r.Length - 2, 1)[0] + r[^1] + r[(r.Length - 1)..][0]);
            """, "4003\n91\n151\n", 0, ""];
        yield return ["f14-k03-field-elem-alias-receivers", """
            var h = new H(Some(1), 3);
            WriteLine(h.O.UnwrapOr(h.N));
            var c = new C(Some(2));
            WriteLine(c.O.UnwrapOr(c.K));
            Vec<Option<int>> v = [Some(5), None];
            WriteLine(v[1].UnwrapOr(v.Count));
            ref Option<int> x = ref h.O;
            WriteLine(x.UnwrapOr(h.N));
            Option<int> p = None;
            WriteLine((x = ref p).UnwrapOr(7));
            WriteLine(x.IsSome);
            WriteLine(Slot(ref h).UnwrapOr(c.K));
            static ref Option<int> Slot(ref H q) => ref q.O;
            struct H(Option<int> o, int n) { public Option<int> O = o; public int N = n; }
            class C(Option<int> o) { public Option<int> O = o; public int K = 9; }
            """, "1\n2\n2\n1\n7\nFalse\n1\n", 0, ""];
        yield return ["f14-k04-noncopy-receivers-accept", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(new Box(2)).V);
            Option<Box> q = None;
            WriteLine(q.UnwrapOr(new Box(3)).V);
            Option<Box> e = Some(new Box(4));
            WriteLine(e.Expect("x").V);
            Result<Box, int> r = Err(1);
            WriteLine(r.UnwrapOr(new Box(6)).V);
            public sealed class Box(int v) { public int V = v; }
            """, "1\n3\n4\n6\n", 0, ""];
        yield return ["f14-k05-rvalue-receivers", """
            Option<int> o = Some(1);
            WriteLine(Opt(4).UnwrapOr(Clear(ref o)));
            WriteLine(o.IsSome);
            Result<int, int> r = Ok(2);
            WriteLine(Id(r).UnwrapOr(Fail(ref r)));
            static Option<int> Opt(int v) => Some(v);
            static int Clear(ref Option<int> x) { x = None; return 5; }
            static int Fail(ref Result<int, int> x) { x = Err(1); return 6; }
            static Result<int, int> Id(Result<int, int> x) => x;
            """, "4\nFalse\n2\n", 0, ""];
        yield return ["f14-k07-loop-elem-receiver", """
            Vec<Option<int>> v = [Some(1), None, Some(3)];
            int sum = 0;
            for (int i = 0; i < v.Count; i++) sum += v[i].UnwrapOr(i * 10);
            WriteLine(sum);
            Option<int> n = None;
            WriteLine(n.Expect("boom"));
            """, "14\n", 101, "panic: boom\n"];
        yield return ["f14-k08-this-field-receivers", """
            var s = new S(None, 4);
            WriteLine(s.M());
            var c = new C(Some(6));
            WriteLine(c.M());
            WriteLine(Get(Some(7), 1) + Get(None, 2));
            WriteLine(Get(Some(new Box(8)), new Box(0)).V);
            static T Get<T>(Option<T> o, T d) => o.UnwrapOr(d);
            struct S(Option<int> o, int k) { public Option<int> O = o; public int K = k; public readonly int M() => O.UnwrapOr(K); }
            class C(Option<int> o) { public Option<int> O = o; public int K = 3; [Mut] public int M() => O.UnwrapOr(K); }
            public sealed class Box(int v) { public int V = v; }
            """, "4\n6\n9\n8\n", 0, ""];
        yield return ["f14-k09-refassign-recv-repointed-in-arg", """
            Option<int> o = Some(1);
            Option<int> d = Some(2);
            ref Option<int> x = ref d;
            WriteLine((x = ref o).UnwrapOr((x = ref d).UnwrapOr(5)));
            WriteLine(x.UnwrapOr(0));
            Result<int, int> r = Ok(3);
            Result<int, int> e = Ok(4);
            ref Result<int, int> y = ref e;
            WriteLine((y = ref r).Expect((y = ref e).IsOk ? "ok" : "err"));
            """, "1\n2\n3\n", 0, ""];
        yield return ["h055-ext-byval-recv-assignexpr-arg", """
            ReadOnlySpan<byte> r = "abc".AsUtf8();
            WriteLine(r.StartsWith((r = "xyz".AsUtf8()).Slice(0, 1)));
            WriteLine(r.IndexOf((r = "qq".AsUtf8()).Slice(0, 1)));
            """, "False\n-1\n", 0, ""];
        yield return ["r18-bytespan-ext-byval-assignexpr", """
            ReadOnlySpan<byte> r = "abcab".AsUtf8();
            WriteLine(r.SequenceEqual((r = "xy".AsUtf8()).Slice(0, 2)));
            r = "abcab".AsUtf8();
            WriteLine(r.EndsWith((r = "ab".AsUtf8()).Slice(0, 1)));
            r = "abcab".AsUtf8();
            WriteLine(r.LastIndexOf((r = "b".AsUtf8())[0]));
            r = "abcab".AsUtf8();
            WriteLine(r.Contains((r = "zq".AsUtf8()).Slice(0, 1)));
            r = "abcab".AsUtf8();
            WriteLine(r.StartsWith((r = "zz".AsUtf8()).Length == 2 ? "ab" : "zz"));
            """, "False\nFalse\n4\nFalse\nTrue\n", 0, ""];
        yield return ["r40-ctrl-conditional-receiver", """
            Option<int> o = Some(1);
            Option<int> p = Some(2);
            bool b = o.IsSome;
            WriteLine((b ? o : p).UnwrapOr(Clear(ref o)));
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "1\n", 0, ""];
        yield return ["r41-ctrl-call-result-receiver", """
            Option<int> o = Some(1);
            WriteLine(Id(o).UnwrapOr(Clear(ref o)));
            static Option<int> Id(Option<int> x) => x;
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "1\n", 0, ""];
        yield return ["r42-ctrl-move-receiver", """
            Option<int> o = Some(1);
            WriteLine(Move(o).UnwrapOr(Clear(ref o)));
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "1\n", 0, ""];
        yield return ["r44-property-operand-then-write", """
            Option<int> o = Some(1);
            WriteLine(o.IsSome == Clear(ref o));
            Option<int> p = Some(2);
            WriteLine(p.Unwrap() * 10 + Fill(ref p));
            Option<int> q = Some(3);
            WriteLine((q is Some<int>) == Clear(ref q));
            Option<int> s = Some(4);
            WriteLine((s switch { Some<int>(var x) => x, _ => -1 }) * 10 + Fill(ref s));
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [4];
            ReadOnlySpan<int> r = a.AsReadOnlySpan();
            WriteLine(r.Length * 10 + (r = b.AsReadOnlySpan()).Length);
            static bool Clear(ref Option<int> x) { x = None; return true; }
            static int Fill(ref Option<int> x) { x = Some(9); return 7; }
            """, "True\n27\nTrue\n47\n31\n", 0, ""];
        yield return ["p10-span-write-index-reads-elem", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            s[s[0]] = 7;
            s[^s[0]] = 8;
            WriteLine(a[1]);
            WriteLine(a[2]);
            """, "7\n8\n", 0, ""];
        yield return ["p15-ctrl-noncopy-cond-arg", """
            Option<Box> o = Some(new Box(1));
            Option<Box> p = None;
            bool f = Id(true);
            WriteLine(o.UnwrapOr(f ? new Box(2) : new Box(3)).V);
            WriteLine(p.UnwrapOr(f ? new Box(2) : new Box(3)).V);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "1\n2\n", 0, ""];
        yield return ["p50-ctrl-linear-expect", """
            Option<Res> o = Some(new Res(1));
            bool f = Id(true);
            using var r = o.Expect(f ? "a" : "b");
            WriteLine(r.Id);
            Option<Res> p = Some(new Res(2));
            using var q = p.Expect("m");
            WriteLine(q.Id);
            static bool Id(bool b) => b;
            public sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "1\n2\n-2\n-1\n", 0, ""];
        yield return ["gp-ros-index-param-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [7, 8, 9, 10];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), Id(true), Num(1)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, bool f, int k)
            {
                return r[(r = q).Length - 3];
            }
            static bool Id(bool b) => b;
            static int Num(int n) => n;
            """, "8\n", 0, ""];
        yield return ["gp-ros-slice1-param-cond", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [7, 8, 9, 10];
            WriteLine(G(a.AsReadOnlySpan(), b.AsReadOnlySpan(), Id(true), Num(1)));
            static int G(ReadOnlySpan<int> r, ReadOnlySpan<int> q, bool f, int k)
            {
                return r.Slice(f ? (r = q).Length - 3 : 1)[0];
            }
            static bool Id(bool b) => b;
            static int Num(int n) => n;
            """, "8\n", 0, ""];
        yield return ["t10-refassign-vec-recv-cond", """
            Vec<int> v = [1];
            Vec<int> w = [2];
            ref Vec<int> xv = ref w;
            bool f = Id(true);
            (xv = ref v).Add(f ? 5 : 6);
            xv.Add(f ? 7 : 8);
            WriteLine(v.Count * 10 + w.Count);
            WriteLine(v[1] + v[2]);
            static bool Id(bool b) => b;
            """, "31\n12\n", 0, ""];
        yield return ["t11-refassign-user-struct-cond", """
            var s = new S(1);
            var t = new S(2);
            ref S x = ref t;
            bool f = Id(true);
            WriteLine((x = ref s).Plus(f ? 3 : 4));
            (x = ref s).Bump(f ? 3 : 4);
            WriteLine(s.N * 10 + t.N);
            static bool Id(bool b) => b;
            struct S(int n) { public int N = n; public readonly int Plus(int k) => N * 1000 + k; public void Bump(int k) { N += k; } }
            """, "1003\n42\n", 0, ""];
    }

    [Theory]
    [MemberData(nameof(ReceiverControls))]
    [MemberData(nameof(ReceiverControlsD1w))]
    public async Task ReceiverControls_AcceptWithNativeParity(string shape, string source, string expected, int exit, string error)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected, exit, error);
        Assert.True(result.Managed.Exit == exit, $"{shape}: managed exit {result.Managed.Exit}");
    }

    public static IEnumerable<object[]> ReceiverArgumentsD1w()
    {
        yield return ["n101-r01-out-local-unwrapor", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Fresh(out o)).V);
            WriteLine(o.Unwrap().V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r02-out-local-expect-none", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.Expect(Gone(out o)).V);
            static string Gone(out Option<Box> o) { o = None; return "gone"; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r03-assign-expr-in-arg", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick((o = Some(new Box(8))).IsSome)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r05-nested-unwrapor-out", """
            Option<Box> o = Some(new Box(5));
            Option<Box> p = None;
            WriteLine(o.UnwrapOr(p.UnwrapOr(Fresh(out o))).V);
            WriteLine(Show(ref o));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r06-none-receiver-out-fills", """
            Option<Box> o = None;
            WriteLine(o.UnwrapOr(Fresh(out o)).V);
            WriteLine(Show(ref o));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r07-result-unwrapor-out", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(r.UnwrapOr(FreshR(out r)).V);
            static Box FreshR(out Result<Box, int> r) { r = Ok(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r08-result-expect-out-err", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(r.Expect(GoneR(out r)).V);
            static string GoneR(out Result<Box, int> r) { r = Err(3); return "gone"; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r09-result-assign-expr", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(r.UnwrapOr(Pick((r = Ok(new Box(8))).IsOk)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r10-valparam-out", """
            WriteLine(G(Some(new Box(5))));
            static int G(Option<Box> o) => o.UnwrapOr(Fresh(out o)).V;
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r11-valparam-assign", """
            WriteLine(G(Some(new Box(5))));
            static int G(Option<Box> o) => o.UnwrapOr(Pick((o = Some(new Box(8))).IsSome)).V;
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r20-branch-reinit-taken", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(true) ? Fresh(out o) : new Box(2)).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r21-branch-reinit-not-taken", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(false) ? Fresh(out o) : new Box(2)).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r22-branch-assign-taken", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(true) ? Pick((o = Some(new Box(8))).IsSome) : new Box(2)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r23-or-assign", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick(Id(false) || (o = Some(new Box(8))).IsSome)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r24-both-branches-reinit-then-use", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(true) ? Fresh(out o) : Fresh(out o)).V);
            WriteLine(Show(ref o));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r25-heap-payload-out-lsan", """
            Option<Vec<int>> o = Some(Mk(5));
            WriteLine(o.UnwrapOr(FreshV(out o)).Count);
            WriteLine(o.UnwrapOr(Mk(1)).Count);
            static Vec<int> Mk(int n) { Vec<int> v = []; for (int i = 0; i < n; i++) { v.Add(i); } return v; }
            static Vec<int> FreshV(out Option<Vec<int>> o) { o = Some(Mk(8)); return Mk(1); }
            """, "VIB003"];
        yield return ["n101-r28-loop-out", """
            Option<Box> o = Some(new Box(5));
            int t = 0;
            for (int i = 0; i < 3; i++) { t += o.UnwrapOr(Fresh(out o)).V; }
            WriteLine(t);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r29-expect-assign-expr", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.Expect(Msg((o = None).IsSome)).V);
            static string Msg(bool b) => b ? "some" : "none";
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r31-switch-arg-out", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(true) switch { true => Fresh(out o), _ => new Box(2) }).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r32-and-assign", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick(Id(true) && (o = Some(new Box(8))).IsSome)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r34-result-valparam-out", """
            WriteLine(G(Ok(new Box(5))));
            static int G(Result<Box, int> r) => r.UnwrapOr(FreshR(out r)).V;
            static Box FreshR(out Result<Box, int> r) { r = Ok(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r38-result-branch-out", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(r.UnwrapOr(Id(true) ? FreshR(out r) : new Box(2)).V);
            static Box FreshR(out Result<Box, int> r) { r = Ok(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r39-expect-valparam-out", """
            WriteLine(G(Some(new Box(5))));
            static int G(Option<Box> o) => o.Expect(Gone(out o)).V;
            static string Gone(out Option<Box> o) { o = None; return "gone"; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n101-r40-copy-twin-out", """
            Option<int> o = Some(5);
            WriteLine(o.UnwrapOr(FreshI(out o)));
            static int FreshI(out Option<int> o) { o = Some(8); return 1; }
            """, "VIB003"];
        yield return ["n101-r41-copy-twin-assign", """
            Option<int> o = Some(5);
            WriteLine(o.UnwrapOr((o = Some(8)).UnwrapOr(1)));
            """, "VIB003"];
        yield return ["n101-u01-ref-use", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Rd(ref o)).V);
            static Box Rd(ref Option<Box> o) => new Box(o.IsSome ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-u02-in-use", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Rd(in o)).V);
            static Box Rd(in Option<Box> o) => new Box(o.IsSome ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-u03-move-use", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Mv(o)).V);
            static Box Mv(Option<Box> o) => new Box(o.IsSome ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-u04-ref-write-through-ref", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Wr(ref o)).V);
            static Box Wr(ref Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-u05-read-then-out", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick2(o.IsSome, Fresh(out o))).V);
            static Box Pick2(bool b, Box x) => b ? x : new Box(2);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-r33-nested-same-receiver-out", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(o.UnwrapOr(Fresh(out o))).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-r36-replace-in-arg", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick(o.Replace(Some(new Box(8))).IsSome)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-r37-asmut-set-in-arg", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick(Put(o.AsMut()))).V);
            static bool Put(OptionMut<Box> m) { m.Set(Some(new Box(8))); return true; }
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e01-noncopy-arg-clears", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(Clear(ref o)).V);
            static Box Clear(ref Option<Box> x) { x = None; return new Box(5); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e02-noncopy-result-expect-arg-writes", """
            Result<Box, int> r = Ok(new Box(1));
            WriteLine(r.Expect(Msg(ref r)).V);
            static string Msg(ref Result<Box, int> x) { x = Err(3); return "gone"; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e03-noncopy-cond-arg-reads", """
            Option<Box> o = Some(new Box(1));
            bool f = Id(true);
            WriteLine(o.UnwrapOr(f ? Make(o.IsSome) : new Box(2)).V);
            static Box Make(bool b) => new Box(b ? 7 : 8);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e09-linear-expect-arg-uses-recv", """
            Option<Res> o = Some(new Res(1));
            using var r = o.Expect(Text(o.IsSome));
            WriteLine(r.Id);
            static string Text(bool b) => b ? "a" : "b";
            public sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "VIB001"];
        yield return ["programs4-e11-arg-is-moved-value-then-used", """
            Option<Box> o = None;
            Box b = new Box(4);
            WriteLine(o.UnwrapOr(b).V);
            WriteLine(b.V);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e15-param-recv-arg-writes", """
            WriteLine(G(Some(new Box(1))).V);
            static Box G(Option<Box> o) => o.UnwrapOr(Clear(ref o));
            static Box Clear(ref Option<Box> x) { x = None; return new Box(5); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e17-nested-noncopy-same", """
            Option<Box> o = Some(new Box(6));
            WriteLine(o.UnwrapOr(o.UnwrapOr(new Box(2))).V);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e21-expect-noncopy-arg-reads-cond", """
            Option<Box> o = Some(new Box(1));
            bool f = Id(true);
            WriteLine(o.Expect(f ? Text(o.IsSome) : "x").V);
            static string Text(bool b) => b ? "a" : "b";
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs4-e23-noncopy-recv-then-reuse", """
            Option<Box> o = Some(new Box(1));
            WriteLine(o.UnwrapOr(new Box(2)).V);
            WriteLine(o.IsSome);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-r12-refparam-out", """
            Option<Box> o = Some(new Box(5));
            WriteLine(G(ref o));
            WriteLine(Show(ref o));
            static int G(ref Option<Box> o) => o.UnwrapOr(Fresh(out o)).V;
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r13-refparam-assign", """
            Option<Box> o = Some(new Box(5));
            WriteLine(G(ref o));
            WriteLine(Show(ref o));
            static int G(ref Option<Box> o) => o.UnwrapOr(Pick((o = Some(new Box(8))).IsSome)).V;
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r14-structfield-recv-out-whole", """
            S s = new S(Some(new Box(5)));
            WriteLine(s.O.UnwrapOr(Reset(out s)).V);
            WriteLine(Show(ref s.O));
            static Box Reset(out S s) { s = new S(Some(new Box(8))); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public struct S(Option<Box> o) { public Option<Box> O = o; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r16-structfield-recv-out-field", """
            S s = new S(Some(new Box(5)));
            WriteLine(s.O.UnwrapOr(Fresh(out s.O)).V);
            WriteLine(Show(ref s.O));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public struct S(Option<Box> o) { public Option<Box> O = o; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r17-structfield-recv-mut-member-resets", """
            S2 s = new S2(Some(new Box(5)));
            WriteLine(s.O.UnwrapOr(s.Reset()).V);
            WriteLine(Show(ref s.O));
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public struct S2(Option<Box> o) { public Option<Box> O = o; [Mut] public Box Reset() { O = Some(new Box(8)); return new Box(1); } }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r18-this-field-struct-mut-member", """
            H h = new H(Some(new Box(5)));
            WriteLine(h.Go());
            public struct H(Option<Box> f) { public Option<Box> F = f; [Mut] public int Go() => F.UnwrapOr(Zap()).V; [Mut] Box Zap() { F = Some(new Box(8)); return new Box(1); } }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r19-this-field-class-member", """
            C c = new C();
            WriteLine(c.Go());
            public sealed class C { public Option<Box> F = Some(new Box(5)); [Mut] public int Go() => F.UnwrapOr(Zap()).V; [Mut] Box Zap() { F = Some(new Box(8)); return new Box(1); } }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-k03-sibling-field-out", """
            T2 s = new T2();
            s.O = Some(new Box(5));
            WriteLine(s.O.UnwrapOr(Fresh(out s.P)).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public struct T2 { public Option<Box> O; public Option<Box> P; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["n101-r15-structfield-recv-assign-whole", """
            S s = new S(Some(new Box(5)));
            WriteLine(s.O.UnwrapOr(Pick((s = new S(Some(new Box(8)))).O.IsSome)).V);
            WriteLine(Show(ref s.O));
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public struct S(Option<Box> o) { public Option<Box> O = o; }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB005"];
        yield return ["n101-m01-cursor-set-out-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            OptionMut<Box> m = a.AsMut();
            m.Set(Some(Rp(out m, b.AsMut())));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static Box Rp(out OptionMut<Box> m, OptionMut<Box> other) { m = other; return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB000"];
        yield return ["n101-m03-cursor-replace-out-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            OptionMut<Box> m = a.AsMut();
            Option<Box> old = m.Replace(Some(Rp(out m, b.AsMut())));
            WriteLine(Show(ref old));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static Box Rp(out OptionMut<Box> m, OptionMut<Box> other) { m = other; return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB000"];
        yield return ["n101-m02-cursor-set-assign-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            OptionMut<Box> m = a.AsMut();
            m.Set(Some(Pick((m = b.AsMut()).IsSome)));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-m04-cursor-param-set-assign-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            G(a.AsMut(), b.AsMut());
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static void G(OptionMut<Box> m, OptionMut<Box> n) { m.Set(Some(Pick((m = n).IsSome))); }
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-m05-cursor-set-branch-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            OptionMut<Box> m = a.AsMut();
            m.Set(Some(Id(true) ? Pick((m = b.AsMut()).IsSome) : new Box(2)));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static bool Id(bool b) => b;
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-m06-cursor-replace-assign-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            OptionMut<Box> m = a.AsMut();
            Option<Box> old = m.Replace(Some(Pick((m = b.AsMut()).IsSome)));
            WriteLine(Show(ref old));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-m08-cursor-param-replace-assign-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            WriteLine(G(a.AsMut(), b.AsMut()));
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static int G(OptionMut<Box> m, OptionMut<Box> n) { Option<Box> old = m.Replace(Some(Pick((m = n).IsSome))); return Show(ref old); }
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-m09-cursor-param-set-helper-repoints", """
            Option<Box> a = Some(new Box(5));
            Option<Box> b = Some(new Box(6));
            G(a.AsMut(), b.AsMut());
            WriteLine(Show(ref a));
            WriteLine(Show(ref b));
            static void G(OptionMut<Box> m, OptionMut<Box> n) { m.Set(Some(Two(new Box(1), (m = n).IsSome))); }
            static Box Two(Box x, bool b) => x;
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["n101-r04-assign-expr-second-position", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick2(3, (o = Some(new Box(8))).IsSome)).V);
            WriteLine(Show(ref o));
            static Box Pick2(int k, bool b) => new Box(b ? k : 2);
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB003"];
        yield return ["n101-r30-linear-expect-out", """
            Option<Res> o = Some(new Res(5));
            using Res a = o.Expect(GoneL(out o));
            WriteLine(a.Id);
            static string GoneL(out Option<Res> o) { o = None; return "gone"; }
            public struct Res(int id) : System.IDisposable { public int Id = id; public void Dispose() { } }
            """, "VIB000,VIB003,VIB006"];
        yield return ["w01-write-then-read", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick2(Fresh(out o), o.IsSome)).V);
            static Box Pick2(Box x, bool b) => b ? x : new Box(2);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["w05-assign-call-result", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Pick((o = Make(8)).IsSome)).V);
            static Option<Box> Make(int v) => Some(new Box(v));
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["w06-assign-take-result", """
            Option<Box> o = Some(new Box(5));
            Option<Box> p = Some(new Box(8));
            WriteLine(o.UnwrapOr(Pick((o = p.Take()).IsSome)).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["w07-inner-receiver-written-by-inner-arg", """
            Option<Box> o = Some(new Box(5));
            Option<Box> p = Some(new Box(6));
            WriteLine(o.UnwrapOr(p.UnwrapOr(Fresh(out p))).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["w02-mixed-branch-use-write", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Id(true) ? Rd(ref o) : Fresh(out o)).V);
            static Box Rd(ref Option<Box> o) => new Box(o.IsSome ? 1 : 2);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001,VIB003"];
        yield return ["w03-out-param-receiver", """
            WriteLine(G(out Option<Box> p).V);
            static Box G(out Option<Box> o) { o = Some(new Box(5)); return o.UnwrapOr(Fresh(out o)); }
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            public sealed class Box(int v) { public int V = v; }
            """, "VIB005"];
        yield return ["w04-intrinsic-out-isnone", """
            Option<Box> o = Some(new Box(5));
            Option<Option<Box>> pp = Some<Option<Box>>(Some(new Box(8)));
            WriteLine(o.UnwrapOr(Pick(pp.IsNone(out o))).V);
            static Box Pick(bool b) => new Box(b ? 1 : 2);
            public sealed class Box(int v) { public int V = v; }
            """, "VIB000,VIB003"];
        yield return ["w18-use-in-branch-then-write-after-join", """
            Option<Box> o = Some(new Box(5));
            bool f = Id(true);
            WriteLine(o.UnwrapOr(Pick2(f ? o.IsSome : true, Fresh(out o))).V);
            static Box Pick2(bool b, Box x) => b ? x : new Box(2);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["w23-use-in-else-branch-then-write-after-join", """
            Option<Box> o = Some(new Box(5));
            bool f = Id(true);
            WriteLine(o.UnwrapOr(Pick2(f ? true : o.IsSome, Fresh(out o))).V);
            static Box Pick2(bool b, Box x) => b ? x : new Box(2);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "VIB001"];
        yield return ["programs-a10-patbind-local-recv-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            bool f = Id(true);
            switch (oo)
            {
                case Some<Option<int>>(var inner):
                    WriteLine(inner.UnwrapOr(f ? Clear(ref inner) : 3));
                    WriteLine(inner.IsSome);
                    break;
                default:
                    WriteLine(-1);
                    break;
            }
            static int Clear(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["programs-a11-patbind-local-recv-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            switch (oo)
            {
                case Some<Option<int>>(var inner):
                    WriteLine(inner.UnwrapOr(Clear(ref inner)));
                    break;
                default:
                    WriteLine(-1);
                    break;
            }
            static int Clear(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["programs-a12-is-patbind-local-recv-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            bool f = Id(true);
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(f ? Clear(ref inner) : 3));
                WriteLine(inner.IsSome);
            }
            static int Clear(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["programs-a13-patbind-result-recv-or", """
            Option<Result<int, int>> oo = Some<Result<int, int>>(Ok(1));
            bool f = Id(false);
            if (oo is Some<Result<int, int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(f || Fail(ref inner) ? 5 : 3));
                WriteLine(inner.IsOk);
            }
            static bool Fail(ref Result<int, int> x) { x = Err(9); return true; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb01-option-unwrapor-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(Id(true) ? W(ref inner) : 3));
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb01-option-unwrapor-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(W(ref inner)));
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["pb-pb02-result-unwrapor-cond", """
            Option<Result<int, int>> oo = Some<Result<int, int>>(Ok(1));
            if (oo is Some<Result<int, int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(Id(true) ? WR(ref inner) : 3));
            }
            static int WR(ref Result<int, int> x) { x = Err(9); return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb02-result-unwrapor-plain", """
            Option<Result<int, int>> oo = Some<Result<int, int>>(Ok(1));
            if (oo is Some<Result<int, int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(WR(ref inner)));
            }
            static int WR(ref Result<int, int> x) { x = Err(9); return 5; }
            """, "VIB003"];
        yield return ["pb-pb12-direct-assign-in-arg-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(Id(true) ? (inner = None).UnwrapOr(5) : 3));
            }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb12-direct-assign-in-arg-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr((inner = None).UnwrapOr(5)));
            }
            """, "VIB003"];
        yield return ["pb-pb16-list-pattern-or-is-var-cond", """
            Option<int> oo = Some(1);
            if (oo is var inner)
            {
                WriteLine(inner.UnwrapOr(Id(true) ? W(ref inner) : 3));
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb16-list-pattern-or-is-var-plain", """
            Option<int> oo = Some(1);
            if (oo is var inner)
            {
                WriteLine(inner.UnwrapOr(W(ref inner)));
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-B-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner)) { WriteLine(inner.UnwrapOr(Id(true) ? W(ref inner) : 3)); }
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-B-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner)) { WriteLine(inner.UnwrapOr(W(ref inner))); }
            static int W(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-U-cond", """
            Option<P> oo = Some<P>(new P(1));
            if (oo is Some<P>(var inner)) { WriteLine(inner.Plus(Id(true) ? W(ref inner) : 3)); }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-U-plain", """
            Option<P> oo = Some<P>(new P(1));
            if (oo is Some<P>(var inner)) { WriteLine(inner.Plus(W(ref inner))); }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-M-cond", """
            Option<P> oo = Some<P>(new P(1));
            if (oo is Some<P>(var inner)) { WriteLine(inner.Bump(Id(true) ? W(ref inner) : 3)); }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c22-is-patbind-local-M-plain", """
            Option<P> oo = Some<P>(new P(1));
            if (oo is Some<P>(var inner)) { WriteLine(inner.Bump(W(ref inner))); }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-B-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            switch (oo)
            {
                case Some<Option<int>>(var inner):
                    WriteLine(inner.UnwrapOr(Id(true) ? W(ref inner) : 3));
                    break;
                default:
                    break;
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-B-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            switch (oo)
            {
                case Some<Option<int>>(var inner):
                    WriteLine(inner.UnwrapOr(W(ref inner)));
                    break;
                default:
                    break;
            }
            static int W(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-U-cond", """
            Option<P> oo = Some<P>(new P(1));
            switch (oo)
            {
                case Some<P>(var inner):
                    WriteLine(inner.Plus(Id(true) ? W(ref inner) : 3));
                    break;
                default:
                    break;
            }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-U-plain", """
            Option<P> oo = Some<P>(new P(1));
            switch (oo)
            {
                case Some<P>(var inner):
                    WriteLine(inner.Plus(W(ref inner)));
                    break;
                default:
                    break;
            }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-M-cond", """
            Option<P> oo = Some<P>(new P(1));
            switch (oo)
            {
                case Some<P>(var inner):
                    WriteLine(inner.Bump(Id(true) ? W(ref inner) : 3));
                    break;
                default:
                    break;
            }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c23-switch-arm-patbind-local-M-plain", """
            Option<P> oo = Some<P>(new P(1));
            switch (oo)
            {
                case Some<P>(var inner):
                    WriteLine(inner.Bump(W(ref inner)));
                    break;
                default:
                    break;
            }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-B-cond", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            WriteLine(G(ref oo));
            static int G(ref Option<Option<int>> q) { if (q is Some<Option<int>>(var inner)) { return inner.UnwrapOr(Id(true) ? W(ref inner) : 3); } return -1; }
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-B-plain", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            WriteLine(G(ref oo));
            static int G(ref Option<Option<int>> q) { if (q is Some<Option<int>>(var inner)) { return inner.UnwrapOr(W(ref inner)); } return -1; }
            static int W(ref Option<int> x) { x = None; return 5; }
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-U-cond", """
            Option<P> oo = Some<P>(new P(1));
            WriteLine(G(ref oo));
            static int G(ref Option<P> q) { if (q is Some<P>(var inner)) { return inner.Plus(Id(true) ? W(ref inner) : 3); } return -1; }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-U-plain", """
            Option<P> oo = Some<P>(new P(1));
            WriteLine(G(ref oo));
            static int G(ref Option<P> q) { if (q is Some<P>(var inner)) { return inner.Plus(W(ref inner)); } return -1; }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-M-cond", """
            Option<P> oo = Some<P>(new P(1));
            WriteLine(G(ref oo));
            static int G(ref Option<P> q) { if (q is Some<P>(var inner)) { return inner.Bump(Id(true) ? W(ref inner) : 3); } return -1; }
            static int W(ref P x) { x = new P(40); return 5; }
            static bool Id(bool b) => b;
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["n90-c44-patbind-of-refparam-option-M-plain", """
            Option<P> oo = Some<P>(new P(1));
            WriteLine(G(ref oo));
            static int G(ref Option<P> q) { if (q is Some<P>(var inner)) { return inner.Bump(W(ref inner)); } return -1; }
            static int W(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; [Mut] public int Bump(int d) { X += d; return X; } }
            """, "VIB003"];
        yield return ["programs8-u01-patbind-user-readonly-member-cond", """
            Option<P> o = Some(new P(1));
            bool f = o.IsSome;
            if (o is Some<P>(var inner))
            {
                WriteLine(inner.Plus(f ? Set(ref inner) : 3));
                WriteLine(inner.X);
            }
            static int Set(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; }
            """, "VIB003"];
        yield return ["programs8-u02-patbind-user-readonly-member-plain", """
            Option<P> o = Some(new P(1));
            if (o is Some<P>(var inner))
            {
                WriteLine(inner.Plus(Set(ref inner)));
            }
            static int Set(ref P x) { x = new P(40); return 5; }
            public struct P(int x) { public int X = x; public readonly int Plus(int d) => X + d; }
            """, "VIB003"];
        yield return ["programs8-u03-patbind-recv-expect-switch-arg", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            int k = oo.IsSome ? 1 : 2;
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.Expect(k switch { 1 => Msg(ref inner), _ => "x" }));
            }
            static string Msg(ref Option<int> x) { x = None; return "gone"; }
            """, "VIB003"];
        yield return ["b11-binding-ref-arg-branch-writes", """
            Option<int> o = Some(4);
            if (o is Some<int>(var x))
            {
                WriteLine(F(ref x, Id(true) ? W(ref x) : 3));
            }
            static int F(ref int a, int b) { a += b; return a; }
            static int W(ref int a) { a = 100; return 1; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["b14-binding-in-arg-branch-writes", """
            Option<int> o = Some(4);
            if (o is Some<int>(var x))
            {
                WriteLine(G(in x, Id(true) ? W(ref x) : 3));
            }
            static int G(in int a, int b) => a * 10 + b;
            static int W(ref int a) { a = 100; return 1; }
            static bool Id(bool b) => b;
            """, "VIB003"];
        yield return ["pb-pb03-vec-index-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var inner))
            {
                WriteLine(inner[Id(true) ? WV(ref inner) : 0]);
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb04-vec-range-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var inner))
            {
                WriteLine(inner[(Id(true) ? WV(ref inner) : 0)..].Length);
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb05-vec-hat-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var inner))
            {
                WriteLine(inner[^(Id(true) ? WV(ref inner) : 1)]);
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb06-vec-ref-index-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var inner))
            {
                ref int e = ref inner[Id(true) ? WV(ref inner) : 0];
                WriteLine(e);
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb09-switch-vec-index-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            switch (oo)
            {
                case Some<Vec<int>>(var inner):
                    WriteLine(inner[Id(true) ? WV(ref inner) : 0]);
                    break;
                default:
                    break;
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb11-ok-binding-vec-index-cond", """
            Result<Vec<int>, int> rr = Ok(Mk());
            if (rr is Ok<Vec<int>>(var inner))
            {
                WriteLine(inner[Id(true) ? WV(ref inner) : 0]);
            }
            static int WV(ref Vec<int> w) { w = [70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210, 220, 230]; return 1; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb13-vec-count-then-index-cond", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var inner))
            {
                WriteLine(inner[Id(true) ? Cl(ref inner) : 0]);
            }
            static int Cl(ref Vec<int> w) { w = []; return 2; }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB003,VIB004"];
        yield return ["pb-pb14-noncopy-option-unwrapor-cond", """
            Option<Option<Vec<int>>> oo = Some<Option<Vec<int>>>(Some(Mk()));
            if (oo is Some<Option<Vec<int>>>(var inner))
            {
                WriteLine(inner.UnwrapOr(Id(true) ? Tk(ref inner) : Mk()).Count);
            }
            static Vec<int> Tk(ref Option<Vec<int>> x) { x = None; return Mk(); }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "VIB004,VIB005"];
        yield return ["inspan-i03-in-span-range", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[1..][0];
            """, "VIB004"];
        yield return ["inspan-i04-in-span-slice", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s.Slice(1)[0];
            """, "VIB004"];
        yield return ["inspan-i07-in-span-range-cond", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[(f ? 1 : 0)..][0];
            """, "VIB004"];
        yield return ["inspan-i08-in-span-slice-cond", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s.Slice(f ? 1 : 0)[0];
            """, "VIB004"];
        yield return ["inspan-i18-in-span-elem-write", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan());
            WriteLine(a[1]);
            static void F(in Span<int> s) { s[1] = 9; }
            """, "VIB004"];
        yield return ["i21-in-span-elem-write-cond", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { s[f ? 1 : 0] = 9; }
            """, "VIB004"];
        yield return ["i22-in-span-compound-cond", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { s[f ? 1 : 0] += 9; }
            """, "VIB004"];
        yield return ["i23-in-span-ref-elem-cond", """
            Vec<int> a = [1, 2, 3];
            F(a.AsSpan(), a.Count > 0);
            WriteLine(a[1]);
            static void F(in Span<int> s, bool f) { ref int e = ref s[f ? 1 : 0]; e = 9; }
            """, "VIB004"];
        yield return ["i29-in-span-slice-cond-length", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s.Slice(f ? 1 : 0, 1).Length;
            """, "VIB004"];
        yield return ["steps-q07-span-hat-write-index-rebinds-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Span<int> s = a.AsSpan();
            Span<int> q = b.AsSpan();
            s[^((s = q).Length - 2)] = 7;
            WriteLine(Sum(a.AsReadOnlySpan()));
            WriteLine(Sum(b.AsReadOnlySpan()));
            static int Sum(ReadOnlySpan<int> r) { int t = 0; foreach (int x in r) { t = t * 10 + x; } return t; }
            """, "VIB003"];
        yield return ["steps-q08-span-hat-compound-index-rebinds-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Span<int> s = a.AsSpan();
            Span<int> q = b.AsSpan();
            s[^((s = q).Length - 2)] += 7;
            WriteLine(Sum(a.AsReadOnlySpan()));
            WriteLine(Sum(b.AsReadOnlySpan()));
            static int Sum(ReadOnlySpan<int> r) { int t = 0; foreach (int x in r) { t = t * 10 + x; } return t; }
            """, "VIB003"];
        yield return ["steps-q11-ros-hat-ref-readonly-index-rebinds-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            ReadOnlySpan<int> s = a.AsReadOnlySpan();
            ReadOnlySpan<int> q = b.AsReadOnlySpan();
            ref readonly int e = ref s[^((s = q).Length - 2)];
            WriteLine(e);
            """, "VIB003"];
        yield return ["steps-q15-span-hat-write-arg-moves-plain", """
            Vec<int> a = [1, 2, 3];
            Vec<int> b = [10, 20, 30, 40, 50];
            Span<int> s = a.AsSpan();
            Span<int> q = b.AsSpan();
            s[^Eat(s)] = 7;
            WriteLine(Sum(a.AsReadOnlySpan()));
            static int Eat(Span<int> x) { x[0] = 9; return 1; }
            static int Sum(ReadOnlySpan<int> r) { int t = 0; foreach (int x in r) { t = t * 10 + x; } return t; }
            """, "VIB003"];
    }

    public static IEnumerable<object[]> ReceiverControlsD1w()
    {
        yield return ["n101-k01-sibling-local-out", """
            Option<Box> o = Some(new Box(5));
            Option<Box> p = None;
            WriteLine(o.UnwrapOr(Fresh(out p)).V);
            WriteLine(Show(ref p));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "5\n8\n", 0, ""];
        yield return ["n101-k02-reinit-after-call", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(new Box(1)).V);
            o = Some(new Box(8));
            WriteLine(o.UnwrapOr(new Box(1)).V);
            WriteLine(Fresh(out o).V);
            WriteLine(Show(ref o));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "5\n8\n1\n8\n", 0, ""];
        yield return ["n101-k04-out-declared-in-arg", """
            Option<Box> o = Some(new Box(5));
            WriteLine(o.UnwrapOr(Fresh(out Option<Box> q)).V);
            WriteLine(Show(ref q));
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "5\n8\n", 0, ""];
        yield return ["n101-m07-cursor-param-take-control", """
            Option<Box> a = Some(new Box(5));
            OptionMut<Box> m = a.AsMut();
            m.Set(m.Take());
            WriteLine(Show(ref a));
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "5\n", 0, ""];
        yield return ["programs4-e04-noncopy-vec-payload-controls", """
            Option<Bag> o = Some(new Bag(3));
            Option<Bag> n = None;
            var x = o.UnwrapOr(new Bag(10));
            var y = n.UnwrapOr(new Bag(20));
            WriteLine(x.Sum() * 100 + y.Sum());
            Result<Bag, Bag> r = Err(new Bag(4));
            var z = r.UnwrapOr(new Bag(5));
            WriteLine(z.Sum());
            Result<Bag, Bag> k = Ok(new Bag(6));
            WriteLine(k.Expect("ok").Sum());
            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
                public readonly int Sum() { int s = 0; for (int i = 0; i < V.Count; i++) { s += V[i]; } return s; }
            }
            """, "810\n15\n21\n", 0, ""];
        yield return ["programs4-e05-noncopy-arg-panics-after-move", """
            Option<Bag> o = Some(new Bag(3));
            WriteLine(1);
            var x = o.UnwrapOr(Boom());
            WriteLine(x.V.Count);
            static Bag Boom() { Option<int> none = None; int v = none.Expect("boom"); return new Bag(v); }
            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
            }
            """, "1\n", 101, "panic: boom\n"];
        yield return ["programs4-e06-noncopy-cond-arg-drops", """
            bool f = Id(true);
            bool g = Id(false);
            Option<Bag> o = Some(new Bag(3));
            Option<Bag> n = None;
            var x = o.UnwrapOr(f ? new Bag(10) : new Bag(11));
            var y = n.UnwrapOr(g ? new Bag(20) : new Bag(21));
            var z = Mk(g).UnwrapOr(f && g ? new Bag(1) : new Bag(2));
            WriteLine(x.V.Count * 10000 + y.V.Count * 100 + z.V.Count);
            static Option<Bag> Mk(bool some) => some ? Some(new Bag(9)) : None;
            static bool Id(bool b) => b;

            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
            }
            """, "32102\n", 0, ""];
        yield return ["programs4-e07-noncopy-loop-reassign", """
            Option<Bag> o = Some(new Bag(1));
            int acc = 0;
            for (int i = 0; i < 5; i++)
            {
                var b = o.UnwrapOr(i switch { 0 => new Bag(7), 1 => new Bag(8), _ => new Bag(9) });
                acc += b.V.Count;
                o = i % 2 == 0 ? None : Some(new Bag(i));
            }
            WriteLine(acc);
            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
            }
            """, "22\n", 0, ""];
        yield return ["programs4-e10-linear-expect-panic-arg", """
            Option<Res> o = Some(new Res(1));
            using var r = o.Expect(Boom());
            WriteLine(r.Id);
            static string Boom() { Option<int> none = None; return none.Expect("boom") > 0 ? "a" : "b"; }
            public sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }
            """, "", 101, "panic: boom\n"];
        yield return ["programs4-e12-arg-is-moved-value-ok", """
            Option<Box> o = None;
            Option<Box> s = Some(new Box(1));
            Box b = new Box(4);
            Box c = new Box(5);
            WriteLine(o.UnwrapOr(b).V * 10 + s.UnwrapOr(Move(c)).V);
            public sealed class Box(int v) { public int V = v; }
            """, "41\n", 0, ""];
        yield return ["programs4-e16-nested-noncopy-distinct", """
            Option<Box> o = None;
            Option<Box> q = Some(new Box(6));
            Option<Box> w = None;
            WriteLine(o.UnwrapOr(q.UnwrapOr(w.UnwrapOr(new Box(2)))).V);
            public sealed class Box(int v) { public int V = v; }
            """, "6\n", 0, ""];
        yield return ["programs4-e18-result-noncopy-err-drop", """
            bool f = Id(true);
            Result<Bag, Bag> r = Err(new Bag(4));
            Result<Bag, Bag> k = Ok(new Bag(6));
            var x = r.UnwrapOr(f ? new Bag(1) : new Bag(2));
            var y = k.UnwrapOr(f ? new Bag(1) : new Bag(2));
            var z = Mk(f).Expect(f ? "a" : "b");
            WriteLine(x.V.Count * 100 + y.V.Count * 10 + z.V.Count);
            static Result<Bag, Bag> Mk(bool ok) => ok ? Ok(new Bag(3)) : Err(new Bag(8));
            static bool Id(bool b) => b;

            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
            }
            """, "163\n", 0, ""];
        yield return ["programs4-e19-noncopy-switch-arg-panic-branch", """
            Option<Bag> o = Some(new Bag(3));
            int k = Num(2);
            WriteLine(0);
            var x = o.UnwrapOr(k switch { 1 => new Bag(1), 2 => Boom(), _ => new Bag(3) });
            WriteLine(x.V.Count);
            static int Num(int n) => n;
            static Bag Boom() { Option<int> none = None; int v = none.Expect("boom"); return new Bag(v); }
            public struct Bag
            {
                public Vec<int> V;
                public Bag(int n) { V = new Vec<int>(); for (int i = 0; i < n; i++) { V.Add(i + 1); } }
            }
            """, "0\n", 101, "panic: boom\n"];
        yield return ["programs4-e24-noncopy-generic-unmanaged-copy", """
            WriteLine(G<int>(Some(4), 1));
            WriteLine(G<long>(None, 7));
            static T G<T>(Option<T> o, T d) where T : unmanaged => o.UnwrapOr(o.IsSome ? d : d);
            """, "4\n7\n", 0, ""];
        yield return ["programs4-e25-string-payload-result", """
            Result<int, string> r = Err("bad");
            bool f = Id(true);
            WriteLine(r.UnwrapOr(f ? (r.IsOk ? 1 : 2) : 3));
            WriteLine(r.IsOk);
            static bool Id(bool b) => b;
            """, "2\nFalse\n", 0, ""];
        yield return ["w08-sibling-arg-reinit-after-call", """
            Option<Box> o = Some(new Box(5));
            WriteLine(Pair(o.UnwrapOr(new Box(1)), Fresh(out o)));
            WriteLine(Show(ref o));
            static int Pair(Box a, Box b) => a.V * 10 + b.V;
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(2); }
            static int Show(ref Option<Box> o) => o.AsRef() switch { Some<Box>(var x) => x.V, _ => -1 };
            public sealed class Box(int v) { public int V = v; }
            """, "52\n8\n", 0, ""];
        yield return ["w09-loop-reinit-after-call", """
            Option<Box> o = Some(new Box(5));
            int sum = 0;
            for (int i = 0; i < 3; i++)
            {
                sum += o.UnwrapOr(Id(i > 0) ? new Box(i) : new Box(7)).V;
                o = i == 1 ? None : Some(new Box(10 + i));
            }
            WriteLine(sum);
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "17\n", 0, ""];
        yield return ["w10-expect-result-branch-sibling-out", """
            Result<Box, int> r = Ok(new Box(5));
            Result<Box, int> q = Err(1);
            WriteLine(r.Expect(Id(true) ? Msg(out q) : "b").V);
            WriteLine(q.IsOk);
            static string Msg(out Result<Box, int> x) { x = Ok(new Box(8)); return "a"; }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "5\nTrue\n", 0, ""];
        yield return ["w12-cast-receiver-branch-out", """
            Option<Box> o = Some(new Box(5));
            WriteLine(((Option<Box>)o).UnwrapOr(Id(true) ? Fresh(out o) : new Box(2)).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "5\n", 0, ""];
        yield return ["w13-assign-receiver-branch-out", """
            Option<Box> o = None;
            Option<Box> p = Some(new Box(5));
            WriteLine((o = p).UnwrapOr(Id(true) ? Fresh(out o) : new Box(2)).V);
            static Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "5\n", 0, ""];
        yield return ["w14-cast-result-receiver-branch-out", """
            Result<Box, int> r = Ok(new Box(5));
            WriteLine(((Result<Box, int>)r).UnwrapOr(Id(true) ? FreshR(out r) : new Box(2)).V);
            static Box FreshR(out Result<Box, int> r) { r = Ok(new Box(8)); return new Box(1); }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "5\n", 0, ""];
        yield return ["w15-cast-receiver-branch-expect", """
            Option<Box> o = Some(new Box(5));
            WriteLine(((Option<Box>)o).Expect(Id(true) ? Gone(out o) : "x").V);
            static string Gone(out Option<Box> o) { o = None; return "gone"; }
            static bool Id(bool b) => b;
            public sealed class Box(int v) { public int V = v; }
            """, "5\n", 0, ""];
        yield return ["pbk1-copy-binding-branching-read", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            if (oo is Some<Option<int>>(var inner))
            {
                WriteLine(inner.UnwrapOr(Id(true) ? inner.UnwrapOr(3) + 1 : 4));
                WriteLine(inner.Expect(Id(false) ? "a" : (inner.IsSome ? "b" : "c")));
            }
            static bool Id(bool b) => b;
            """, "1\n1\n", 0, ""];
        yield return ["pbk2-vec-binding-branching-index-read", """
            Option<Vec<int>> oo = Some<Vec<int>>(Mk());
            if (oo is Some<Vec<int>>(var v))
            {
                WriteLine(v[Id(true) ? v.Count - 1 : 0] + v[^(Id(false) ? 1 : 2)]);
            }
            static Vec<int> Mk() { Vec<int> v = [1, 2, 3]; return v; }
            static bool Id(bool b) => b;
            """, "5\n", 0, ""];
        yield return ["pbk3-binding-branching-writes-other-local", """
            Option<Option<int>> oo = Some<Option<int>>(Some(1));
            Option<int> other = Some(9);
            int total = 0;
            for (int i = 0; i < 2; i++)
            {
                if (oo is Some<Option<int>>(var inner))
                {
                    total += inner.UnwrapOr(Id(i == 0) ? W(ref other) : 3);
                }
            }
            WriteLine(total * 10 + (other.IsSome ? 1 : 0));
            static int W(ref Option<int> x) { x = None; return 5; }
            static bool Id(bool b) => b;
            """, "20\n", 0, ""];
        yield return ["pbk4-switch-binding-user-member-branching-read", """
            Option<P> op = Some(new P(4));
            int r = op switch { Some<P>(var p) => p.Plus(Id(true) ? p.N : 1), _ => 0 };
            WriteLine(r);
            static bool Id(bool b) => b;
            public struct P(int n) { public int N = n; public readonly int Plus(int k) => N * 10 + k; }
            """, "44\n", 0, ""];
        yield return ["inspan-i05-in-span-index-read-cond", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[f ? 1 : 0];
            """, "2\n", 0, ""];
        yield return ["inspan-i06-in-span-hat-read-cond", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[^(f ? 1 : 2)];
            """, "3\n", 0, ""];
        yield return ["inspan-i10-in-span-ref-readonly-elem", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) { ref readonly int e = ref s[f ? 1 : 0]; return e; }
            """, "2\n", 0, ""];
        yield return ["inspan-i12-in-span-option-elem-recv-cond", """
            Vec<Option<int>> a = [Some(1), None];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<Option<int>> s, bool f) => s[0].UnwrapOr(f ? 7 : 8) + s[f ? 1 : 0].UnwrapOr(5);
            """, "6\n", 0, ""];
        yield return ["inspan-i20-readonly-struct-member-span-param", """
            Vec<int> a = [1, 2, 3];
            var h = new H(2);
            WriteLine(h.Pick(a.AsSpan(), a.Count > 0));
            public struct H(int k) { public int K = k; public readonly int Pick(in Span<int> s, bool f) => s[f ? K : 0]; }
            """, "3\n", 0, ""];
        yield return ["inspan-i01-in-span-index-read", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[1];
            """, "2\n", 0, ""];
        yield return ["inspan-i02-in-span-hat-read", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s[^1];
            """, "3\n", 0, ""];
        yield return ["inspan-i09-in-span-length", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<int> s, bool f) => s.Length + (s.IsEmpty ? 1 : 0);
            """, "3\n", 0, ""];
        yield return ["inspan-i11-in-span-foreach", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan()));
            static int F(in Span<int> s) { int t = 0; foreach (int x in s) { t += x; } return t; }
            """, "6\n", 0, ""];
        yield return ["inspan-i15-in-vec-controls", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(in a, a.Count > 0));
            static int F(in Vec<int> s, bool f) => s[f ? 1 : 0] + s[^(f ? 1 : 2)] * 10;
            """, "32\n", 0, ""];
        yield return ["inspan-i17-in-ros-controls", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsReadOnlySpan(), a.Count > 0));
            static int F(in ReadOnlySpan<int> s, bool f) => s[f ? 1 : 0] + s[^(f ? 1 : 2)] * 10 + s[(f ? 1 : 0)..][0] * 100 + s.Slice(f ? 1 : 0)[1] * 1000;
            """, "3232\n", 0, ""];
        yield return ["i26-in-span-switch-and-index-reads", """
            Vec<int> a = [1, 2, 3];
            WriteLine(F(a.AsSpan(), a.Count > 0, a.Count > 5));
            static int F(in Span<int> s, bool f, bool g) => s[f switch { true => 1, _ => 0 }] * 100 + s[f && g ? 2 : 0] * 10 + s[f || g ? 2 : 1];
            """, "213\n", 0, ""];
        yield return ["i27-in-span-option-elem-expect-cond", """
            Vec<Option<int>> a = [Some(4), None];
            WriteLine(F(a.AsSpan(), a.Count > 0));
            static int F(in Span<Option<int>> s, bool f) => s[f ? 0 : 1].Expect(f ? "a" : "b") + s[1].UnwrapOr(f ? 20 : 30);
            """, "24\n", 0, ""];
        yield return ["i28-byvalue-span-param-branching-controls", """
            Vec<int> a = [1, 2, 3];
            bool f = a.Count > 0;
            F(a.AsSpan(), f);
            WriteLine(a[0] * 100 + a[1] * 10 + a[2]);
            static void F(Span<int> s, bool f) { s[f ? 1 : 0] = 9; s[f ? 2 : 0] += 1; ref int e = ref s[f ? 0 : 1]; e = 7; Span<int> t = s[(f ? 1 : 0)..]; t[0] += 0; }
            """, "794\n", 0, ""];
        yield return ["programs9-m03-span-two-in-args-branching", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            bool f = Id(true);
            bool g = Id(false);
            WriteLine(Sum(in s[f ? 0 : 1], in s[g ? 1 : 2]));
            static int Sum(in int x, in int y) => x * 10 + y;
            static bool Id(bool b) => b;
            """, "13\n", 0, ""];
        yield return ["programs9-m04-span-elem-assign-both-sides-branching", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            bool f = Id(true);
            bool g = Id(false);
            s[f ? 0 : 1] = s[g ? 1 : 2];
            s[f ? 1 : 0] += s[g ? 0 : 2];
            s[g ? 0 : 2]++;
            WriteLine(s[0] * 100 + s[1] * 10 + s[2]);
            static bool Id(bool b) => b;
            """, "354\n", 0, ""];
        yield return ["programs9-m05-span-ros-view-live-branching-read", """
            Vec<int> a = [1, 2, 3];
            Span<int> s = a.AsSpan();
            bool f = Id(true);
            ReadOnlySpan<int> ro = s;
            int y = s[f ? 1 : 2];
            WriteLine(ro[0] + y * 10);
            static bool Id(bool b) => b;
            """, "21\n", 0, ""];
        yield return ["programs9-m06-span-param-branching-reads", """
            Vec<int> a = [1, 2, 3, 4];
            WriteLine(G(a.AsSpan(), Id(true)));
            static int G(Span<int> s, bool f)
            {
                ref readonly int x = ref s[f ? 0 : 1];
                int y = Add(s[f ? 1 : 2], s[f ? 2 : 3]);
                return x + y * 10 + s.Slice(f ? 1 : 0, f ? 2 : 1).Length * 1000;
            }
            static int Add(int p, int q) => p + q;
            static bool Id(bool b) => b;
            """, "2051\n", 0, ""];
        yield return ["programs9-m14-span-pipelines", """
            Vec<int> a = [5, 4, 3, 2, 1, 0];
            Span<int> s = a.AsSpan();
            bool f = Id(true);
            int k = Num(1);
            WriteLine(s.Slice(1).Slice(1)[0] + s[1..][..2][1] * 10 + s[1..^1].Length * 100);
            WriteLine(s[s[s[5] + 4]] + s[f ? s[4] : s[5]] * 10 + s[(k switch { 1 => 2, _ => 3 })..].Length * 100);
            Sort(s);
            ReadOnlySpan<int> r = a.AsReadOnlySpan();
            WriteLine(r.Slice(f ? r[4] : 0, k > 0 && r.Length > 3 ? 2 : 1)[1] + r[^(f ? r[3] : 1)] * 10);
            WriteLine(a[0] * 100 + a[3] * 10 + a[5]);
            static int Num(int n) => n;
            static void Sort(Span<int> t)
            {
                for (int i = 0; i < t.Length; i++)
                {
                    for (int j = i + 1; j < t.Length; j++)
                    {
                        if (t[j] < t[i]) { int tmp = t[i]; t[i] = t[j]; t[j] = tmp; }
                    }
                }
            }
            static bool Id(bool b) => b;
            """, "433\n444\n35\n35\n", 0, ""];
    }

    public static IEnumerable<object[]> OverlappingCallOperands()
    {
        yield return ["n68-01-two-cursors-witness", """
            WriteLine(F(0, false));
            static int F(int p0, bool p1)
            {
                int acc = 0;
                Option<Cell1> o2 = Some(new Cell1(p0));
                acc += M7(in acc, 0);
                acc += Two9(o2.AsMut(), o2.AsMut());
                return 0;
            }
            static int M7(in int p, int k) { return 0; }
            static int Two9(OptionMut<Cell1> a, OptionMut<Cell1> b) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v)
            {
                public int V = v;
            }
            """, "VIB002"];
        yield return ["n68-02-three-cursors", """
            Option<Cell1> o = Some(new Cell1(1));
            WriteLine(Three(o.AsMut(), o.AsMut(), o.AsMut()));
            static int Three(OptionMut<Cell1> a, OptionMut<Cell1> b, OptionMut<Cell1> c) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002"];
        yield return ["n68-03-optref-then-optmut", """
            Option<Cell1> o = Some(new Cell1(1));
            WriteLine(RM(o.AsRef(), o.AsMut()));
            static int RM(OptionRef<Cell1> a, OptionMut<Cell1> b) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002"];
        yield return ["n68-04-optmut-then-optref", """
            Option<Cell1> o = Some(new Cell1(1));
            WriteLine(MR(o.AsMut(), o.AsRef()));
            static int MR(OptionMut<Cell1> a, OptionRef<Cell1> b) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002"];
        yield return ["n68-05-instance-method-two-cursors", """
            Option<Cell1> o = Some(new Cell1(1));
            var k = new K();
            WriteLine(k.Two(o.AsMut(), o.AsMut()));
            public sealed class K { public int Two(OptionMut<Cell1> a, OptionMut<Cell1> b) => a.IsSome ? 1 : 0; }
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002"];
        yield return ["n68-06-struct-receiver-cursor-into-field", """
            var h = new H(Some(new Cell1(1)));
            WriteLine(h.Use(h.O.AsMut()));
            struct H(Option<Cell1> o) { public Option<Cell1> O = o; public readonly int Use(OptionMut<Cell1> m) => m.IsSome ? 1 : 0; }
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002"];
        yield return ["n68-07-ctrl-distinct-options", """
            Option<Cell1> o = Some(new Cell1(1));
            Option<Cell1> p = None;
            WriteLine(Two(o.AsMut(), p.AsMut()));
            WriteLine(Two(o.AsMut(), o.AsMut().IsSome ? p.AsMut() : p.AsMut()));
            static int Two(OptionMut<Cell1> a, OptionMut<Cell1> b) => (a.IsSome ? 1 : 0) + (b.IsSome ? 10 : 0);
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB003"];
        yield return ["n68-08-ros-and-refstruct-span-same-vec", """
            Vec<int> v = [1, 2];
            WriteLine(Pair(v.AsReadOnlySpan(), new W(v.AsSpan())));
            static int Pair(ReadOnlySpan<int> r, W w) { w.S[0] = 9; return r[0]; }
            ref struct W(Span<int> s) { public Span<int> S = s; }
            """, "VIB000,VIB003"];
        yield return ["n68-09-cursor-local-and-derived-ref", """
            Option<Cell1> o = Some(new Cell1(1));
            var m = o.AsMut();
            WriteLine(Use(m.AsRef(), m));
            static int Use(OptionRef<Cell1> a, OptionMut<Cell1> b) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002,VIB003"];
        yield return ["n68-10-optref-temp-and-cursor-local", """
            Option<Cell1> o = Some(new Cell1(1));
            var m = o.AsMut();
            WriteLine(RM(o.AsRef(), m));
            static int RM(OptionRef<Cell1> a, OptionMut<Cell1> b) => a.IsSome ? 1 : 0;
            public sealed class Cell1(int v) { public int V = v; }
            """, "VIB002,VIB003"];
        yield return ["n68-11-span-temp-and-span-temp", """
            Vec<int> v = [1, 2];
            WriteLine(Two(v.AsSpan(), v.AsSpan()));
            static int Two(Span<int> a, Span<int> b) { a[0] = 9; return b[0]; }
            """, "VIB002"];
        yield return ["n68-12-ros-temp-and-span-temp", """
            Vec<int> v = [1, 2];
            WriteLine(Two(v.AsReadOnlySpan(), v.AsSpan()));
            static int Two(ReadOnlySpan<int> a, Span<int> b) { b[0] = 9; return a[0]; }
            """, "VIB002"];
    }

    [Theory]
    [MemberData(nameof(OverlappingCallOperands))]
    public Task OverlappingCallOperands_HaveExactRoslynIds(string shape, string source, string ids) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, ids);

    // F14 matrix: each receiver form of Option and Result UnwrapOr and Expect, and each Span and ReadOnlySpan site,
    // with the argument plain and behind ?:, &&, || and switch. An argument that writes the receiver is exactly
    // VIB003; the same shapes that only read it are accepted with native parity.
    private static readonly (string Name, string Code)[] ArgumentShapes =
        [("plain", "{0}"), ("cond", "f ? {0} : 1"), ("and", "f && {0} > 0 ? 1 : 2"), ("or", "!f || {0} > 0 ? 1 : 2"), ("switch", "k switch {{ 1 => {0}, _ => 1 }}")];

    private static readonly (string Name, string Type, string Value, string Cleared)[] ReceiverTypes =
        [("o", "Option<int>", "Some(1)", "None"), ("r", "Result<int, int>", "Ok(1)", "Err(2)")];

    // {T} type, {n} name, {v} value, {body} the calls; Receiver is what they are made on, Place what an argument writes.
    private static readonly (string Name, string Code, string Receiver, string Place, string Types)[] ReceiverForms =
    [
        ("local", "{T} {n} = {v};\n{body}", "{n}", "{n}", ""),
        ("reflocal", "{T} {n} = {v};\nref {T} {n}a = ref {n};\n{body}", "{n}a", "{n}a", ""),
        ("refassign", "{T} {n} = {v};\n{T} {n}d = {v};\nref {T} {n}x = ref {n}d;\n{body}", "({n}x = ref {n})", "{n}", ""),
        ("structfield", "var {n}h = new H{n}({v});\n{body}", "{n}h.V", "{n}h.V", "struct H{n}({T} v) { public {T} V = v; }\n"),
        ("vecelem", "Vec<{T}> {n}v = [{v}];\n{body}", "{n}v[0]", "{n}v[0]", ""),
        ("classfield", "var {n}c = new C{n}({v});\n{body}", "{n}c.V", "{n}c.V", "public sealed class C{n}({T} v) { public {T} V = v; }\n"),
        ("param", "Run{n}({v}, f, k);\nstatic void Run{n}({T} {n}, bool f, int k)\n{\n{body}}\n", "{n}", "{n}", ""),
        ("refparam", "{T} {n}x = {v};\nRun{n}(ref {n}x, f, k);\nstatic void Run{n}(ref {T} {n}, bool f, int k)\n{\n{body}}\n", "{n}", "{n}", ""),
    ];

    private static readonly (string Name, string Code)[] SpanReads =
        [("index", "n += r[{0}];"), ("slice", "n += r.Slice({0})[0];"), ("slice2", "n += r.Slice({0}, 1)[0];"), ("hat", "n += r[^({0})];"), ("range", "n += r[({0})..][0];")];

    private static readonly (string Name, string Code)[] SpanWrites =
        [("write", "r[{0}] = 5;"), ("compound", "r[{0}] += 5;"), ("incr", "r[{0}]++;"), ("ref", "{{ ref int e = ref r[{0}]; e = 5; }}")];

    public static IEnumerable<object[]> ReceiverMatrixWrites()
    {
        foreach (var form in ReceiverForms)
        {
            foreach (var type in ReceiverTypes)
            {
                foreach (string member in (string[])["UnwrapOr", "Expect"])
                {
                    foreach (var shape in ArgumentShapes)
                    {
                        string source = ReceiverProgram(form, [type], [member], [shape], "W(ref {p})") +
                            $"static int W(ref {type.Type} x) {{ x = {type.Cleared}; return 5; }}\n" + ReceiverTypesOf(form, [type]);
                        yield return [$"{form.Name}-{type.Name}-{member}-{shape.Name}", source];
                    }
                }
            }
        }
        foreach (bool readOnly in (bool[])[true, false])
        {
            foreach (var site in readOnly ? SpanReads : [.. SpanReads, .. SpanWrites])
            {
                foreach (var shape in ArgumentShapes)
                {
                    yield return [$"{(readOnly ? "ros" : "span")}-{site.Name}-{shape.Name}", SpanProgram(readOnly, [site], [shape], $"(r = b.{SpanView(readOnly)}).Length - 3")];
                }
            }
        }
    }

    public static IEnumerable<object[]> ReceiverMatrixReads()
    {
        foreach (var form in ReceiverForms)
        {
            string source = ReceiverProgram(form, ReceiverTypes, ["UnwrapOr", "Expect"], ArgumentShapes, "P({p})") +
                "static int P<T>(T value) => 5;\n" + ReceiverTypesOf(form, ReceiverTypes);
            yield return [form.Name, source, string.Concat(Enumerable.Repeat("1\n", 20))];
        }
        yield return ["ros", SpanProgram(true, SpanReads, ArgumentShapes, "r.Length - 2"), "55\n6\n"];
        yield return ["span", SpanProgram(false, [.. SpanReads, .. SpanWrites], ArgumentShapes, "r.Length - 2"), "55\n9\n"];
    }

    [Theory]
    [MemberData(nameof(ReceiverMatrixWrites))]
    public Task ReceiverMatrixWrite_IsExactlyVib003(string shape, string source) =>
        Round12SwitchGuardBorrowTests.AssertRoslynIdsAsync(shape, source, "VIB003");

    [Theory]
    [MemberData(nameof(ReceiverMatrixReads))]
    public async Task ReceiverMatrixRead_AcceptsWithNativeParity(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }

    private static string ReceiverProgram(
        (string Name, string Code, string Receiver, string Place, string Types) form,
        (string Name, string Type, string Value, string Cleared)[] types,
        string[] members,
        (string Name, string Code)[] shapes,
        string argument)
    {
        var text = new StringBuilder("bool f = Id(true);\nint k = Num(1);\n");
        foreach (var type in types)
        {
            string Fill(string s) => s.Replace("{T}", type.Type).Replace("{n}", type.Name).Replace("{v}", type.Value);
            var body = new StringBuilder();
            foreach (string member in members)
            {
                foreach (var shape in shapes)
                {
                    string arg = string.Format(shape.Code, argument.Replace("{p}", Fill(form.Place)));
                    body.Append($"WriteLine({Fill(form.Receiver)}.{member}({(member == "Expect" ? $"Text({arg})" : arg)}));\n");
                }
            }
            text.Append(Fill(form.Code).Replace("{body}", body.ToString()));
        }
        text.Append("static bool Id(bool x) => x;\nstatic int Num(int x) => x;\n");
        if (members.Contains("Expect"))
        {
            text.Append("static string Text(int n) => n > 0 ? \"m\" : \"z\";\n");
        }
        return text.ToString();
    }

    private static string ReceiverTypesOf((string Name, string Code, string Receiver, string Place, string Types) form, (string Name, string Type, string Value, string Cleared)[] types) =>
        string.Concat(types.Select(type => form.Types.Replace("{T}", type.Type).Replace("{n}", type.Name)));

    private static string SpanView(bool readOnly) => readOnly ? "AsReadOnlySpan()" : "AsSpan()";

    private static string SpanProgram(bool readOnly, (string Name, string Code)[] sites, (string Name, string Code)[] shapes, string index)
    {
        var text = new StringBuilder("bool f = Id(true);\nint k = Num(1);\nint n = 0;\nVec<int> a = [1, 2, 3];\nVec<int> b = [7, 8, 9, 10];\n");
        text.Append($"{(readOnly ? "ReadOnlySpan<int>" : "Span<int>")} r = a.{SpanView(readOnly)};\n");
        foreach (var site in sites)
        {
            foreach (var shape in shapes)
            {
                text.Append(string.Format(site.Code, string.Format(shape.Code, index))).Append('\n');
            }
        }
        return text.Append("WriteLine(n);\nWriteLine(a[0] + a[1] + a[2]);\nstatic bool Id(bool x) => x;\nstatic int Num(int x) => x;\n").ToString();
    }
}
