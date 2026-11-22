using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12RefRepointingTests
{
    public static IEnumerable<object[]> ValidRefRepoints()
    {
        yield return ["c25-precompute-branch-expressions", """
            Span<int> data = [4, 7, 9];
            ref int r = ref data[0];
            int k = 1;
            bool flag = k > 0;

            int i = Index(flag && k > 0);
            r = ref data[i];
            WriteLine(r);
            i = Index(flag || k == 2);
            r = ref data[i];
            WriteLine(r);
            i = flag ? 1 : 2;
            r = ref data[i];
            WriteLine(r);
            i = k switch { 1 => 1, _ => 2 };
            r = ref data[i];
            WriteLine(r);
            i = Index(k is 1);
            r = ref data[i];
            WriteLine(r);
            static int Index(bool b) => b ? 1 : 2;
            """, "7\n7\n7\n7\n7\n"];
        yield return ["c25-branch-before-repoint", """
            int a = 4;
            int b = 7;
            int c = 0;
            ref int r = ref c;
            if (a < b) r = ref a;
            else r = ref b;
            WriteLine(r);
            """, "4\n"];
        yield return ["n50-d3-no-call", """
            Span<int> s = [1, 2];
            int other = 5;
            WriteLine(C.F(ref other, s));
            static class C { public static int F(ref int other, Span<int> s) { ref int sink = ref s[0]; sink = ref s[1]; return sink + other; } }
            """, "7\n"];
        yield return ["n50-d4-no-reassign", """
            Span<int> s = [1, 2];
            int other = 5;
            WriteLine(C.F(ref other, s));
            static class C { public static int F(ref int other, Span<int> s) { ref int sink = ref Pick(other, s); return sink + other; } static ref int Pick(int o, Span<int> s) => ref s[1]; }
            """, "7\n"];
        yield return ["n50-d5-by-value", """
            Span<int> s = [1, 2];
            int other = 5;
            WriteLine(C.F(other, s));
            static class C { public static int F(int other, Span<int> s) { ref int sink = ref s[0]; sink = ref Pick(other, s); return sink + other; } static ref int Pick(int o, Span<int> s) => ref s[1]; }
            """, "7\n"];
        yield return ["n50-d6-other-span", """
            Span<int> s = [1, 2];
            Span<int> t = [3, 4];
            int other = 5;
            WriteLine(C.F(other, s, t));
            static class C { public static int F(int other, Span<int> s, Span<int> t) { ref int sink = ref s[0]; sink = ref Pick(other, t); return sink + other; } static ref int Pick(int o, Span<int> s) => ref s[1]; }
            """, "9\n"];
        yield return ["n50-d7-no-read-after", """
            Span<int> s = [1, 2];
            int other = 5;
            WriteLine(C.F(other, s));
            static class C { public static int F(int other, Span<int> s) { ref int sink = ref s[0]; sink = ref Pick(other, s); return other; } static ref int Pick(int o, Span<int> s) => ref s[1]; }
            """, "5\n"];
        yield return ["n50-d8-write-after", """
            Span<int> s = [1, 2];
            int other = 5;
            WriteLine(C.F(other, s));
            static class C { public static int F(int other, Span<int> s) { ref int sink = ref s[0]; sink = ref Pick(other, s); sink = 9; return s[1] + other; } static ref int Pick(int o, Span<int> s) => ref s[1]; }
            """, "14\n"];
        yield return ["n50-c11-vec-d5", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [p, p + 1, p + 2]; int o = 5; ref int sink = ref v[0]; sink = ref Pick(ref v); return sink + o; } static ref int Pick(ref Vec<int> v) => ref v[1]; }
            """, "10007\n"];
        yield return ["n50-c26-ncst-d5", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            public struct H(Vec<int> a, Vec<int> b) { public Vec<int> A = a; public Vec<int> B = b; }
            static class C { public static int F(int p) { Vec<int> a = [p]; Vec<int> b = [p + 1, p + 2]; H h = new H(a, b); int o = 5; ref Vec<int> sink = ref h.A; sink = ref PickB(ref h); return sink.Count * 10 + sink[0] + o; } static ref Vec<int> PickB(ref H h) => ref h.B; }
            """, "30027\n"];
        yield return ["n50-p04-vec-repoint-readarg", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [p, p + 1, p + 2]; Vec<int> u = [p + 10, p + 20, p + 30]; int o = 5; ref int sink = ref v[0]; sink = ref PickAt(ref u, v.Count - 2); return sink + o; } static ref int PickAt(ref Vec<int> v, int i) => ref v[i]; }
            """, "29026\n"];
        yield return ["n53-n03-span-reborrow-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; int o = 5; ref int sink = ref s[0]; sink = ref Self(ref sink); sink += 1; return sink + o; } static ref int Self(ref int x) => ref x; }
            """, "10007\n"];
        yield return ["n53-n08-other-span-reads-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; Span<int> t = [p + 10, p + 20]; int o = 5; ref int sink = ref s[0]; sink = ref Pick(o, t, sink); return sink + o; } static ref int Pick(int o, Span<int> s, int k) => ref s[k & 1]; }
            """, "19026\n"];
        yield return ["n53-readonlyspan-shared-index", """
            ReadOnlySpan<int> data = [1, 4, 7];
            ref readonly int old = ref data[0];
            old = ref data[old];
            WriteLine(old);
            """, "4\n"];
        yield return ["n53-call-inner-overwrite-unrelated-spans", """
            Span<int> s = [4, 7];
            Span<int> t = [8, 9];
            Span<int> u = [20, 21];
            ref int sink = ref s[0];
            sink = ref Pick((sink = ref t[0]), u);
            s[0] = 13;
            sink += 2;
            WriteLine(sink);
            WriteLine(s[0]);
            static ref int Pick(int i, Span<int> data) => ref data[i & 1];
            """, "22\n13\n"];
        yield return ["n53-call-inner-overwrite-read-through-old-ref", """
            Span<int> s = [4, 7];
            Span<int> t = [8, 9];
            Span<int> u = [20, 21];
            ref int sink = ref s[0];
            sink = ref Pick(sink, (sink = ref t[0]), u);
            WriteLine(sink);
            static ref int Pick(int old, int next, Span<int> data) => ref data[(old + next) & 1];
            """, "20\n"];
        yield return ["n53-call-three-unrelated-first", """
            Span<int> s = [4, 7];
            Span<int> t = [51, 52];
            ref int sink = ref s[0];
            sink = ref Pick(sink, t, 0);
            WriteLine(sink);
            static ref int Pick(int i, Span<int> data, int tag) => ref data[(i + tag) & 1];
            """, "51\n"];
        yield return ["n53-call-three-unrelated-middle", """
            Span<int> s = [4, 7];
            Span<int> t = [51, 52];
            ref int sink = ref s[0];
            sink = ref Pick(0, sink, t);
            WriteLine(sink);
            static ref int Pick(int tag, int i, Span<int> data) => ref data[(i + tag) & 1];
            """, "51\n"];
        yield return ["n53-call-three-unrelated-last", """
            Span<int> s = [4, 7];
            Span<int> t = [51, 52];
            ref int sink = ref s[0];
            sink = ref Pick(0, t, sink);
            WriteLine(sink);
            static ref int Pick(int tag, Span<int> data, int i) => ref data[(i + tag) & 1];
            """, "51\n"];
    }

    [Theory]
    [MemberData(nameof(ValidRefRepoints))]
    public async Task ValidRefRepoint_HasNativeParity(string shape, string source, string expected)
    {
        var result = await Round12RuleWitness.AcceptAsync(source, expected);
        Assert.True(result.Managed.Exit == 0, $"{shape}: managed exit {result.Managed.Exit}");
    }

    public static IEnumerable<object[]> ConflictingLiveLoans()
    {
        yield return ["d05-derived-ref-repoint", """
            Span<int> s = [1, 2];
            Span<int> t = [7, 8];
            ref int sink = ref s[0];
            ref int d = ref sink;
            sink = ref t[1];
            d = 9;
            WriteLine(sink + d + s[0]);
            """, "VIB003"];
        yield return ["f17-nested-mirror", """
            Span<int> s = [4, 7];
            int k = 1;
            ref int r = ref s[0];
            r = ref Pick(s, (r = ref k));
            WriteLine(r);
            static ref int Pick(Span<int> data, int i) => ref data[i];
            """, "VIB003"];
        yield return ["q03-vec-sink-first", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [1, p, 0, 3]; int o = 5; ref int sink = ref v[0]; sink = ref PickAt(sink, ref v); return sink + o; } static ref int PickAt(int i, ref Vec<int> v) => ref v[i]; }
            """, "VIB003"];
        yield return ["n50-p01-vec-loan-touch", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [p, p + 1, p + 2]; int o = 5; ref int a = ref v[0]; int t = Touch(ref v); return a + o + t; } static int Touch(ref Vec<int> v) => v[1]; }
            """, "VIB003"];
        yield return ["n53-n01-span-index-by-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Span<int> s = [1, p, 0]; int o = 5; ref int sink = ref s[0]; sink = ref s[sink]; return sink + o; } }
            """, "VIB003"];
        yield return ["n53-n02-span-call-reads-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; int o = 5; ref int sink = ref s[0]; sink = ref Pick(o, s, sink); return sink + o; } static ref int Pick(int o, Span<int> s, int k) => ref s[(k & 1) + 1]; }
            """, "VIB003"];
        yield return ["n53-n04-vec-index-by-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [1, p, 0]; int o = 5; ref int sink = ref v[0]; sink = ref v[sink]; return sink + o; } }
            """, "VIB003"];
        yield return ["n53-n05-vec-refarg-reads-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Vec<int> v = [1, p, 0, 3]; int o = 5; ref int sink = ref v[0]; sink = ref PickAt(ref v, sink); return sink + o; } static ref int PickAt(ref Vec<int> v, int i) => ref v[i]; }
            """, "VIB003"];
        yield return ["n53-n06-field-reads-sink", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            public struct H(Vec<int> a, Vec<int> b) { public Vec<int> A = a; public Vec<int> B = b; }
            static class C { public static int F(int p) { Vec<int> a = [p]; Vec<int> b = [p + 1, p + 2]; H h = new H(a, b); int o = 5; ref Vec<int> sink = ref h.A; sink = ref PickB(ref h, sink.Count); return sink.Count * 10 + sink[0] + o; } static ref Vec<int> PickB(ref H h, int k) => ref h.B; }
            """, "VIB003"];
        yield return ["n53-n07-loop-index-by-cursor", """
            WriteLine(C.F(1) + C.F(4) * 1000);
            static class C { public static int F(int p) { Span<int> s = [1, 2, 3, p]; int o = 5; ref int cur = ref s[0]; for (int i = 1; i < 3; i++) { cur = ref s[cur]; } return cur + o; } }
            """, "VIB003"];
        yield return ["n53-call-snapshots-old-ref-first", """
            Span<int> s = [4, 7];
            ref int sink = ref s[0];
            sink = ref Pick(sink, s);
            WriteLine(sink);
            static ref int Pick(int i, Span<int> s) => ref s[i - 3];
            """, "VIB003"];
        yield return ["n53-call-reads-old-ref-after-overlap", """
            Vec<int> v = [4, 7];
            Span<int> spare = [8, 12];
            ref int sink = ref v[0];
            sink = ref Pick(Touch(ref v), sink, spare);
            WriteLine(sink);
            static int Touch(ref Vec<int> v) { v[0] += 1; return v[0]; }
            static ref int Pick(int n, int old, Span<int> other) => ref other[n - old];
            """, "VIB003"];
        yield return ["n53-span-from-end-index", """
            Span<int> data = [1, 4, 7];
            ref int old = ref data[0];
            old = ref data[^old];
            WriteLine(old);
            """, "VIB003"];
        yield return ["n53-span-slice-bound", """
            Span<int> data = [1, 4, 7];
            ref int old = ref data[0];
            old = ref data.Slice(old)[0];
            WriteLine(old);
            """, "VIB003"];
        yield return ["n53-span-unused-new-ref", """
            Span<int> data = [1, 4, 7];
            ref int old = ref data[0];
            old = ref data[old];
            WriteLine(4);
            """, "VIB003"];
        yield return ["n53-call-borrowed-span-first", """
            Span<int> s = [4, 7];
            ref int sink = ref s[0];
            sink = ref Pick(s, sink);
            WriteLine(sink);
            static ref int Pick(Span<int> data, int i) => ref data[i & 1];
            """, "VIB003"];
        yield return ["n53-call-inner-overwrite-old-ref", """
            Span<int> s = [4, 7];
            Span<int> t = [8, 9];
            ref int sink = ref s[0];
            sink = ref Pick((sink = ref t[0]), s);
            WriteLine(sink);
            static ref int Pick(int i, Span<int> data) => ref data[i & 1];
            """, "VIB003"];
        yield return ["n53-call-inner-overwrite-after-conflicting-span", """
            Span<int> s = [4, 7];
            Span<int> t = [8, 9];
            ref int sink = ref s[0];
            sink = ref Pick(s, (sink = ref t[0]));
            WriteLine(sink);
            static ref int Pick(Span<int> data, int i) => ref data[i & 1];
            """, "VIB003"];
        yield return ["n53-call-three-borrowed-first", """
            Span<int> s = [4, 7];
            ref int sink = ref s[0];
            sink = ref Pick(sink, s, 0);
            WriteLine(sink);
            static ref int Pick(int i, Span<int> data, int tag) => ref data[(i + tag) & 1];
            """, "VIB003"];
        yield return ["n53-call-three-borrowed-middle", """
            Span<int> s = [4, 7];
            ref int sink = ref s[0];
            sink = ref Pick(0, sink, s);
            WriteLine(sink);
            static ref int Pick(int tag, int i, Span<int> data) => ref data[(i + tag) & 1];
            """, "VIB003"];
        yield return ["n53-call-three-borrowed-last", """
            Span<int> s = [4, 7];
            ref int sink = ref s[0];
            sink = ref Pick(0, s, sink);
            WriteLine(sink);
            static ref int Pick(int tag, Span<int> data, int i) => ref data[(i + tag) & 1];
            """, "VIB003"];
    }

    [Theory]
    [MemberData(nameof(ConflictingLiveLoans))]
    public async Task ReadingOldRef_KeepsItsLoan(string shape, string source, string id)
    {
        var verdict = await Round12RuleWitness.CheckAllAsync(source, requireNoWarnings: true);
        Assert.True(verdict.Stage0 is [var stage0] && stage0 == id, $"{shape}: Stage-0 [{string.Join(",", verdict.Stage0)}]");
        Assert.True(verdict.Analyzer is [var analyzer] && analyzer == id, $"{shape}: Analyzer [{string.Join(",", verdict.Analyzer)}]");
    }
}
