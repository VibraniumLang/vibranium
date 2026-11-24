using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfRefRepointLoanTests
{
    // The Roslyn F17/N58 updates ship independently. The shared harness
    // still checks C# validity before asserting self's decided verdict.
    [Theory]
    [InlineData("n01-span-index-by-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Span<int> s = [1, p, 0]; int o = 5; ref int sink = ref s[0]; sink = ref s[sink]; return sink + o; } }\n")]
    [InlineData("n02-span-call-reads-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; int o = 5; ref int sink = ref s[0]; sink = ref Pick(o, s, sink); return sink + o; } static ref int Pick(int o, Span<int> s, int k) => ref s[(k & 1) + 1]; }\n")]
    [InlineData("n04-vec-index-by-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Vec<int> v = [1, p, 0]; int o = 5; ref int sink = ref v[0]; sink = ref v[sink]; return sink + o; } }\n")]
    [InlineData("n05-vec-refarg-reads-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Vec<int> v = [1, p, 0, 3]; int o = 5; ref int sink = ref v[0]; sink = ref PickAt(ref v, sink); return sink + o; } static ref int PickAt(ref Vec<int> v, int i) => ref v[i]; }\n")]
    [InlineData("n06-field-reads-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\npublic struct H(Vec<int> a, Vec<int> b) { public Vec<int> A = a; public Vec<int> B = b; }\nstatic class C { public static int F(int p) { Vec<int> a = [p]; Vec<int> b = [p + 1, p + 2]; H h = new H(a, b); int o = 5; ref Vec<int> sink = ref h.A; sink = ref PickB(ref h, sink.Count); return sink.Count * 10 + sink[0] + o; } static ref Vec<int> PickB(ref H h, int k) => ref h.B; }\n")]
    [InlineData("n07-loop-index-by-cursor", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Span<int> s = [1, 2, 3, p]; int o = 5; ref int cur = ref s[0]; for (int i = 1; i < 3; i++) { cur = ref s[cur]; } return cur + o; } }\n")]
    [InlineData("span-call-old-ref-first", "Span<int> s = [4, 7];\nref int sink = ref s[0];\nsink = ref Pick(sink, s);\nWriteLine(sink);\nstatic ref int Pick(int i, Span<int> s) => ref s[i - 3];\n")]
    [InlineData("span-call-old-ref-last", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; sink = ref Pick(s, sink); WriteLine(sink); static ref int Pick(Span<int> s, int i) => ref s[i - 3];")]
    [InlineData("span-nested-value-call", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; sink = ref Pick(Id(sink), s); WriteLine(sink); static int Id(int i) => i; static ref int Pick(int i, Span<int> s) => ref s[i - 3];")]
    [InlineData("vec-ref-return-expression", "Vec<int> v = [4, 7, 9]; ref int sink = ref v[0]; int value = (sink = ref C.Pick(sink, ref v)); WriteLine(value); static class C { public static ref int Pick(int i, ref Vec<int> v) => ref v[i - 3]; }")]
    [InlineData("repoint-self-nested-second-argument", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nr = ref Pick(s, (r = ref k));\nWriteLine(r);\nstatic ref int Pick(Span<int> data, int i) => ref data[i];\n")]
    [InlineData("repoint-self-nested-first-argument", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nr = ref Pick((r = ref k), s);\nWriteLine(r);\nstatic ref int Pick(int i, Span<int> data) => ref data[i];\n")]
    [InlineData("repoint-self-nested-assignment-expression", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nint v = (r = ref Pick((r = ref k), s));\nWriteLine(v);\nstatic ref int Pick(int i, Span<int> data) => ref data[i];\n")]
    public async Task DestinationOccurrenceKeepsTheOldLoanThroughTheRhs(string name, string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source, name + ".cs");
        Assert.Equal(new[] { "VIB003" }, result.Self);
    }

    [Theory]
    [InlineData("n03-span-reborrow-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; int o = 5; ref int sink = ref s[0]; sink = ref Self(ref sink); sink += 1; return sink + o; } static ref int Self(ref int x) => ref x; }\n")]
    [InlineData("n08-other-span-reads-sink", "WriteLine(C.F(1) + C.F(4) * 1000);\nstatic class C { public static int F(int p) { Span<int> s = [p, p + 1, p + 2]; Span<int> t = [p + 10, p + 20]; int o = 5; ref int sink = ref s[0]; sink = ref Pick(o, t, sink); return sink + o; } static ref int Pick(int o, Span<int> s, int k) => ref s[k & 1]; }\n")]
    [InlineData("span-namesake-parameter", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; sink = ref C.Pick(s, sink: 1); WriteLine(sink); static class C { public static ref int Pick(Span<int> s, int sink) => ref s[sink]; }")]
    [InlineData("span-namesake-member", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; I i = new I(4); sink = ref Pick(i.sink, s); WriteLine(sink); static ref int Pick(int i, Span<int> s) => ref s[i - 3]; struct I(int i) { public int sink = i; }")]
    [InlineData("noncopy-repoint-unrelated-field", "var h = new H([4], [7, 9]); ref Vec<int> sink = ref h.A; sink = ref Pick(ref h); WriteLine(sink.Count); static ref Vec<int> Pick(ref H h) => ref h.B; struct H(Vec<int> a, Vec<int> b) { public Vec<int> A = a; public Vec<int> B = b; }")]
    [InlineData("vec-repoint-loop-no-read", "Vec<int> v = [4, 7, 9]; ref int r = ref v[0]; for (int j = 0; j < 3; j++) { r = ref v[j]; WriteLine(r); }")]
    [InlineData("repoint-self-nested-unrelated-span", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nSpan<int> t = [8, 9];\nr = ref Pick((r = ref k), t);\nWriteLine(r); WriteLine(s[0]);\nstatic ref int Pick(int i, Span<int> data) => ref data[i];\n")]
    [InlineData("repoint-self-nested-own-carrier-reborrow", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nSpan<int> t = [8, 9];\nr = ref Pick(Bump(ref r), (r = ref k), t);\nWriteLine(r); WriteLine(s[0]);\nstatic int Bump(ref int n) { n++; return n; }\nstatic ref int Pick(int a, int i, Span<int> data) => ref data[(i + a) & 1];\n")]
    [InlineData("repoint-different-carrier-nested", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nint n = 0; ref int other = ref n;\nr = ref Pick(s, (other = ref k));\nWriteLine(r); WriteLine(other);\nstatic ref int Pick(Span<int> data, int i) => ref data[i];\n")]
    public async Task RepointsWithoutAConflictingDestinationUseStillCompile(string name, string source)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source, name + ".cs", requireNoWarnings: true);
        Assert.Empty(result.Self);
    }

    [Theory]
    [InlineData("span-conditional-occurrence", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; bool b = false; sink = /*cut*/ref Pick(b ? sink : 4, s); WriteLine(sink); static ref int Pick(int i, Span<int> s) => ref s[i - 3];")]
    [InlineData("span-shortcircuit-occurrence", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; bool b = false; sink = /*cut*/ref Pick(b && sink > 0 ? 5 : 4, s); WriteLine(sink); static ref int Pick(int i, Span<int> s) => ref s[i - 3];")]
    [InlineData("span-switch-arm-occurrence", "Span<int> s = [4, 7, 9]; ref int sink = ref s[0]; int k = 0; sink = /*cut*/ref Pick(k switch { 1 => sink, _ => 4 }, s); WriteLine(sink); static ref int Pick(int i, Span<int> s) => ref s[i - 3];")]
    [InlineData("n58-conditional-index", "Span<int> data = [1, 2, 3]; bool flag = Choose(); ref int old = ref data[0]; old = /*cut*/ref data[flag ? old : 2]; WriteLine(old); static bool Choose() => false;")]
    [InlineData("n53-call-conditional-not-taken", "WriteLine(F(false));\nstatic int F(bool flag)\n{\n    Span<int> s = [4, 7];\n    ref int sink = ref s[0];\n    sink = /*cut*/ref Pick(flag ? sink : 0, s);\n    return sink;\n}\nstatic ref int Pick(int i, Span<int> data) => ref data[i & 1];\n")]
    [InlineData("n53-call-short-circuit-not-evaluated", "WriteLine(F(false));\nstatic int F(bool flag)\n{\n    Span<int> s = [4, 7];\n    ref int sink = ref s[0];\n    sink = /*cut*/ref Pick(flag && sink > 0, s);\n    return sink;\n}\nstatic ref int Pick(bool second, Span<int> data) => ref data[(second ? 1 : 0)];\n")]
    [InlineData("span-shortcircuit-or-occurrence", "Span<int> s = [4, 7]; ref int sink = ref s[0]; bool flag = true; sink = /*cut*/ref Pick(flag || sink > 0, s); WriteLine(sink); static ref int Pick(bool b, Span<int> s) => ref s[b ? 1 : 0];")]
    [InlineData("span-conditional-other-branch-occurrence", "Span<int> s = [4, 7]; ref int sink = ref s[0]; bool flag = true; sink = /*cut*/ref Pick(flag ? 1 : sink, s); WriteLine(sink); static ref int Pick(int i, Span<int> s) => ref s[i];")]
    [InlineData("span-conditional-nested-argument-occurrence", "Span<int> s = [4, 7]; ref int sink = ref s[0]; bool flag = true; sink = /*cut*/ref Pick(Index(0, flag ? 1 : sink), s); WriteLine(sink); static int Index(int a, int b) => a + b; static ref int Pick(int i, Span<int> s) => ref s[i];")]
    [InlineData("repoint-self-nested-conditional-argument", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nbool flag = F(false);\nr = /*cut*/ref Pick(s, flag ? (r = ref k) : 1);\nWriteLine(r);\nstatic bool F(bool flag) => flag;\nstatic ref int Pick(Span<int> data, int i) => ref data[i];\n")]
    [InlineData("repoint-self-nested-shortcircuit-argument", "Span<int> s = [4, 7]; int k = 1; ref int r = ref s[0];\nbool flag = F(true);\nWriteLine((r = /*cut*/ref Pick(flag || (r = ref k) > 0, s)));\nstatic bool F(bool flag) => flag;\nstatic ref int Pick(bool flag, Span<int> s) => ref s[flag ? 1 : 0];\n")]
    [InlineData("n58-conditional-index-no-local", "Span<int> data = [1, 2, 3]; bool flag = Choose(); ref int old = ref data[0]; old = /*cut*/ref data[flag ? 1 : 2]; WriteLine(old); static bool Choose() => false;")]
    public Task ControlFlowInTheRepointTargetIsC25(string name, string source) =>
        Round12SelfPatternAndRepointCutTests.AssertSelfCutAsync(25, source, name + ".cs", exactIds: true);
}
