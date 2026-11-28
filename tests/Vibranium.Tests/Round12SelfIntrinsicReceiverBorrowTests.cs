using System.Threading.Tasks;
using Xunit;

namespace Vibranium.Tests;

public sealed class Round12SelfIntrinsicReceiverBorrowTests
{
    [Theory]
    [InlineData("Option<N> a = Some(new N(1));\nOption<N> b = Some(new N(2));\nvar m = a.AsMut();\nm.Set(Pick((m = b.AsMut()).IsSome));\nWriteLine(a.Unwrap().V);\nWriteLine(b.Unwrap().V);\nstatic Option<N> Pick(bool f) => f ? Some(new N(9)) : None;\nclass N(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<N> a = Some(new N(1));\nOption<N> b = Some(new N(2));\nvar m = a.AsMut();\nOption<N> old = m.Replace(Pick((m = b.AsMut()).IsSome));\nWriteLine(old.Unwrap().V);\nWriteLine(a.Unwrap().V);\nWriteLine(b.Unwrap().V);\nstatic Option<N> Pick(bool f) => f ? Some(new N(9)) : None;\nclass N(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<N> a = Some(new N(1));\nOption<N> b = Some(new N(2));\nOptionMut<N> m = a.AsMut();\nm.Set((m = b.AsMut()) switch { _ => Some(new N(9)) });\nWriteLine(a.IsSome);\nWriteLine(b.Unwrap().V);\nsealed class N(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(1));\nvar m = o.AsMut();\nm.Set(DropIt(Move(m)));\nstatic Option<Box> DropIt(OptionMut<Box> c) => None;\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(3));\nvar m = o.AsMut();\nm.Set(Eat(m));\nWriteLine(o.Unwrap().V);\nstatic Option<Box> Eat(OptionMut<Box> c) => Some(new Box(5));\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<int> o = Some(1);\nWriteLine(o.UnwrapOr(Clear(ref o)));\nWriteLine(o.IsSome);\nstatic int Clear(ref Option<int> x) { x = None; return 5; }\n", "VIB003")]
    [InlineData("var s = new S(1);\nWriteLine(s.Plus(Set(ref s)));\nstatic int Set(ref S p) { p = new S(100); return 5; }\nstruct S(int n) { public int N = n; public readonly int Plus(int k) => N * 1000 + k; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(1);\nWriteLine(o.Expect(Msg(ref o)));\nstatic string Msg(ref Option<int> x) { x = None; return \"was cleared\"; }\n", "VIB003")]
    [InlineData("Result<int, int> r = Err(3);\nWriteLine(r.Expect(Msg(ref r)));\nstatic string Msg(ref Result<int, int> x) { x = Ok(77); return \"still err\"; }\n", "VIB003")]
    [InlineData("var h = new H(Some(1));\nWriteLine(h.O.UnwrapOr(ClearH(ref h)));\nstatic int ClearH(ref H p) { p.O = None; return 5; }\nstruct H(Option<int> o) { public Option<int> O = o; }\n", "VIB003")]
    [InlineData("var c = new C(Some(1));\nWriteLine(c.O.UnwrapOr(c.Clear()));\nclass C(Option<int> o) { public Option<int> O = o; [Mut] public int Clear() { O = None; return 5; } }\n", "VIB003")]
    [InlineData("Vec<Option<int>> v = [Some(1), Some(2)];\nWriteLine(v[0].UnwrapOr(ClearV(ref v)));\nstatic int ClearV(ref Vec<Option<int>> p) { p[0] = None; return 5; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(1);\nWriteLine(Pair(o.UnwrapOr(Clear(ref o)), 0));\nOption<int> p = Some(2);\nint k = p.UnwrapOr(0) + p.UnwrapOr(Clear(ref p));\nWriteLine(k);\nstatic int Clear(ref Option<int> x) { x = None; return 5; }\nstatic int Pair(int a, int b) => a * 1000 + b;\n", "VIB003")]
    [InlineData("Result<int,int> r = Ok(1); WriteLine(r.UnwrapOr(Change(ref r))); static int Change(ref Result<int,int> r) { r = Err(7); return 2; }", "VIB003")]
    [InlineData("Option<int> o = Some(1); ref Option<int> slot = ref o; WriteLine(o.UnwrapOr(Change(ref slot))); static int Change(ref Option<int> o) { o = None; return 2; }", "VIB003")]
    [InlineData("Option<int> o = Some(1);\nref Option<int> a = ref o;\nWriteLine(a.UnwrapOr(Clear(ref o)));\nstatic int Clear(ref Option<int> x) { x = None; return 5; }\n", "VIB003")]
    [InlineData("Option<int> o = Some(1);\nOption<int> d = None;\nref Option<int> x = ref d;\nWriteLine((x = ref o).UnwrapOr(Clear(ref o)));\nstatic int Clear(ref Option<int> r) { r = None; return 5; }\n", "VIB003")]
    [InlineData("Result<int, int> r = Err(3);\nResult<int, int> d = Ok(0);\nref Result<int, int> x = ref d;\nWriteLine((x = ref r).Expect(SetOk(ref r)));\nstatic string SetOk(ref Result<int, int> p) { p = Ok(77); return \"err\"; }\n", "VIB003")]
    [InlineData("Option<int> a = Some(4); Option<int> b = None; Option<int> c = None; ref Option<int> x = ref b; ref Option<int> y = ref c;\nWriteLine(((x = ref (y = ref a))).Expect(Clear(ref a)));\nstatic string Clear(ref Option<int> a) { a = None; return \"cleared\"; }", "VIB003")]
    [InlineData("Option<int> a = Some(4); Option<int> b = None; ref Option<int> x = ref b;\nWriteLine((x = ref Slot(ref a)).UnwrapOr(Clear(ref a)));\nstatic ref Option<int> Slot(ref Option<int> a) => ref a;\nstatic int Clear(ref Option<int> a) { a = None; return 5; }", "VIB003")]
    [InlineData("Option<int> b = None; ref Option<int> x = ref b; var h = new Holder();\nWriteLine((x = ref h[0]).UnwrapOr(h.Clear()));\nsealed class Holder { public Option<int> Value = Some(3); public ref Option<int> this[int i] { [Mut] get => ref Value; } [Mut] public int Clear() { Value = None; return 5; } }", "VIB003")]
    [InlineData("bool c = Pick(false); Option<int> a = Some(4); Option<int> b = None; ref Option<int> x = ref a;\nWriteLine((x = ref (c ? ref a : ref b)).UnwrapOr(0));\nstatic bool Pick(bool c) => c;", "VIB000")]
    [InlineData("Option<Box> o = Some(new Box(4));\nvar m = o.AsMut();\nif (m is Some<Box>(var x)) { m.Set(None); WriteLine(x.V); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Node> a = Some(new Node(1));\nvar m = a.AsMut();\nm.Set(a.Take());\npublic sealed class Node(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(1));\nWriteLine(o.UnwrapOr(Clear(ref o)).V);\nstatic Box Clear(ref Option<Box> x) { x = None; return new Box(5); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(1));\nWriteLine(o.Expect(Msg(ref o)).V);\nstatic string Msg(ref Option<Box> x) { x = None; return \"was cleared\"; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Result<Box, int> r = Ok(new Box(1));\nWriteLine(r.UnwrapOr(Fail(ref r)).V);\nstatic Box Fail(ref Result<Box, int> x) { x = Err(9); return new Box(5); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(1));\nbool f = Id(true);\nWriteLine(o.Expect(f ? Msg(ref o) : \"b\").V);\nstatic bool Id(bool b) => b;\nstatic string Msg(ref Option<Box> x) { x = None; return \"was cleared\"; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(1));\nbool f = Id(true);\nWriteLine(o.UnwrapOr(f ? Clear(ref o) : new Box(3)).V);\nstatic bool Id(bool b) => b;\nstatic Box Clear(ref Option<Box> x) { x = None; return new Box(5); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Fresh(ref o)).V);\nstatic Box Fresh(ref Option<Box> o) { o = Some(new Box(8)); return new Box(1); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Result<Box, int> r = Ok(new Box(5));\nWriteLine(r.UnwrapOr(Steal(r)).V);\nstatic Box Steal(Result<Box, int> r) => new Box(1);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Eat(o)).V);\nstatic Box Eat(Option<Box> o) => new Box(o.IsSome ? 1 : 2);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Pick(o.AsMut().Take())).V);\nstatic Box Pick(Option<Box> x) => x.UnwrapOr(new Box(1));\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.Expect(o.AsMut().Take().IsSome ? \"a\" : \"b\").V);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(o.AsMut().Take().UnwrapOr(new Box(1))).V);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(o.AsMut().Replace(Some(new Box(8))).UnwrapOr(new Box(1))).V);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.Expect(o.AsMut().Replace(Some(new Box(8))).IsSome ? \"a\" : \"b\").V);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("WriteLine(F(Some(new Box(5))).V);\nstatic Box F(Option<Box> o) => o.UnwrapOr(Pick(o.AsMut().Take()));\nstatic Box Pick(Option<Box> x) => x.UnwrapOr(new Box(1));\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nbool f = Id(true);\nWriteLine(o.UnwrapOr(f ? Pick(o.AsMut().Take()) : new Box(3)).V);\nstatic bool Id(bool b) => b;\nstatic Box Pick(Option<Box> x) => x.UnwrapOr(new Box(1));\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    [InlineData("Option<Box> o = Some(new Box(5));\nvar m = o.AsMut();\nWriteLine(o.UnwrapOr(Pick(m.Take())).V);\nstatic Box Pick(Option<Box> x) => x.UnwrapOr(new Box(1));\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    public async Task SelfRejectsWithDecidedIdSet(string source, string expected)
    {
        // The paired Roslyn change ships independently. The shared witness
        // still proves the source is valid C# before checking self's exact set.
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("Option<int> o = Some(1); WriteLine(o.UnwrapOr(Read(in o))); static int Read(in Option<int> o) => o.IsSome ? 8 : 9;", "1\n")]
    [InlineData("Option<int> o = None; int k = 0; WriteLine(o.UnwrapOr(Bump(ref k))); WriteLine(k); static int Bump(ref int n) { n = 4; return n; }", "4\n4\n")]
    [InlineData("Option<int> o = Some(1); WriteLine(Id(o).UnwrapOr(Clear(ref o))); WriteLine(o.IsSome); static Option<int> Id(Option<int> o) => o; static int Clear(ref Option<int> o) { o = None; return 5; }", "1\nFalse\n")]
    [InlineData("var c = new C(Some(1)); WriteLine(c.O.UnwrapOr(F(ref c.K))); WriteLine(c.K); static int F(ref int x) { x = 7; return x; } class C(Option<int> o) { public Option<int> O = o; public int K = 0; }", "1\n7\n")]
    [InlineData("Option<int> a = Some(4); Option<int> b = None; ref Option<int> x = ref b;\nWriteLine(((x = ref a)).UnwrapOr(Set(ref b))); WriteLine(b.IsSome);\nstatic int Set(ref Option<int> b) { b = Some(8); return 5; }", "4\nTrue\n")]
    [InlineData("Option<int> o = None;\nWriteLine((o = Some(4)).UnwrapOr(Clear(ref o))); WriteLine(o.IsSome);\nstatic int Clear(ref Option<int> o) { o = None; return 5; }", "4\nFalse\n")]
    [InlineData("Option<int> a = Some(4); Option<int> b = None; Option<int> c = None; ref Option<int> x = ref b; ref Option<int> y = ref c;\nWriteLine(((x = ref (y = ref a))).UnwrapOr(Set(ref b))); WriteLine(b.IsSome);\nstatic int Set(ref Option<int> b) { b = Some(8); return 5; }", "4\nTrue\n")]
    [InlineData("Option<Box> a = Some(new Box(1)); Option<Box> b = Some(new Box(2));\nvar m = a.AsMut();\nm.Set(Pick(m.IsSome, ref b));\nWriteLine(m.Replace(Some(new Box(9))).Unwrap().V);\nWriteLine(m.Take().Unwrap().V);\nWriteLine(b.IsSome);\nstatic Option<Box> Pick(bool flag, ref Option<Box> b) { b = None; return flag ? Some(new Box(7)) : None; }\npublic sealed class Box(int v) { public int V = v; }\n", "7\n9\nFalse\n")]
    [InlineData("Option<Box> a = Some(new Box(1));\na.AsMut().Set(Some(new Box(6)));\nWriteLine(a.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "6\n")]
    [InlineData("Option<Node> root = Some(new Node(1, Some(new Node(2, None))));\nvar slot = root.AsMut();\nif (slot is Some<Node>(var n)) slot.Set(n.Next.Take());\nWriteLine(root.Unwrap().V);\npublic sealed class Node(int v, Option<Node> next) { public int V = v; public Option<Node> Next = next; }\n", "2\n")]
    [InlineData("Option<Node> a = Some(new Node(1));\nOption<Node> b = Some(new Node(2));\nvar m = a.AsMut();\nvar other = b.AsMut();\nWriteLine(m.Replace(other.Take()).Unwrap().V);\nWriteLine(other.IsSome);\nWriteLine(m.Take().Unwrap().V);\npublic sealed class Node(int v) { public int V = v; }\n", "1\nFalse\n2\n")]
    [InlineData("Option<Node> a = None;\nOption<int> b = Some(2);\nvar m = a.AsMut();\nm.Set(Pick(b.IsSome));\nWriteLine(a.Unwrap().V);\nstatic Option<Node> Pick(bool flag) => flag ? Some(new Node(4)) : None;\npublic sealed class Node(int v) { public int V = v; }\n", "4\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nm.Set(m.Take());\nWriteLine(m.IsSome);\npublic sealed class Box(int v) { public int V = v; }\n", "True\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nvar old = m.Replace(m.Take());\nWriteLine(old.IsSome);\nWriteLine(m.IsSome);\npublic sealed class Box(int v) { public int V = v; }\n", "False\nTrue\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nvar m = o.AsMut();\nm.Set(Some(new Box(m switch { Some<Box>(var b) => b.V + 1, _ => 0 })));\nWriteLine(o.Unwrap().V);\npublic sealed class Box(int v) { public int V = v; }\n", "10\n")]
    [InlineData("Option<Box> o = Some(new Box(9));\nOption<Box> o2 = Some(new Box(2));\nvar m = o.AsMut();\nm = o2.AsMut();\nm.Set(m.Take());\nWriteLine(o.IsSome);\nWriteLine(o2.IsSome);\npublic sealed class Box(int v) { public int V = v; }\n", "True\nTrue\n")]
    [InlineData("var s = new S(Some(5), 10);\nWriteLine(s.O.UnwrapOr(Bump(ref s.K)));\nWriteLine(s.K);\nstatic int Bump(ref int k) { k = 20; return k; }\nstruct S(Option<int> o, int k) { public Option<int> O = o; public int K = k; }\n", "5\n20\n")]
    [InlineData("Option<Box> o = Some(new Box(5));\nOption<Box> p = Some(new Box(7));\nWriteLine(o.UnwrapOr(Pick(p.AsMut().Take())).V);\nWriteLine(p.IsSome);\nstatic Box Pick(Option<Box> x) => x.UnwrapOr(new Box(1));\npublic sealed class Box(int v) { public int V = v; }\n", "5\nFalse\n")]
    public Task AcceptedControlsHaveNativeParity(string source, string expectedOutput) => Round12RuleWitness.AcceptAsync(source, expectedOutput);

    [Fact]
    public async Task LinearExpectRefWriterRejectsWithExactFrontendParity()
    {
        var result = await Round12RuleWitness.CheckAllAsync(
            "Option<Res> o = Some(new Res(1));\nusing var r = o.Expect(Msg(ref o));\nWriteLine(r.Id);\nstatic string Msg(ref Option<Res> x) { x = None; return \"was cleared\"; }\npublic sealed class Res(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(-Id); } }\n");
        string[] expected = ["VIB001", "VIB006"];
        Assert.Equal(expected, result.Stage0);
        Assert.Equal(expected, result.Analyzer);
        Assert.Equal(expected, result.Self);
    }

    [Theory]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Fresh(out o)).V);\nWriteLine(o.Unwrap().V);\nstatic Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Pick((o = Some(new Box(8))).IsSome)).V);\nstatic Box Pick(bool b) => new Box(b ? 1 : 2);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.Expect(ClearOut(out o)).V);\nstatic string ClearOut(out Option<Box> o) { o = None; return \"cleared\"; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.Expect(Pick((o = None).IsSome)).V);\nstatic string Pick(bool b) => b ? \"a\" : \"b\";\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Result<Box, int> r = Ok(new Box(5));\nWriteLine(r.UnwrapOr(Fresh(out r)).V);\nWriteLine(r.Unwrap().V);\nstatic Box Fresh(out Result<Box, int> r) { r = Ok(new Box(8)); return new Box(1); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Result<Box, int> r = Ok(new Box(5));\nWriteLine(r.Expect(ClearOut(out r)).V);\nstatic string ClearOut(out Result<Box, int> r) { r = Err(9); return \"cleared\"; }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Result<Box, int> r = Ok(new Box(5));\nWriteLine(r.UnwrapOr(Pick((r = Ok(new Box(8))).IsOk)).V);\nstatic Box Pick(bool b) => new Box(b ? 1 : 2);\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("var s = new S(Some(5));\nWriteLine(s.O.UnwrapOr(Reset(out s)));\nWriteLine(s.O.Unwrap());\nstatic int Reset(out S s) { s = new S(Some(8)); return 1; }\nstruct S(Option<int> o) { public Option<int> O = o; }\n", "VIB003")]
    [InlineData("WriteLine(F(Some(new Box(5))).V);\nstatic Box F(Option<Box> o) => o.UnwrapOr(Fresh(out o));\nstatic Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> o = Some(new Box(5));\nbool f = Id(true);\nWriteLine(o.UnwrapOr(f ? Fresh(out o) : new Box(3)).V);\nstatic bool Id(bool b) => b;\nstatic Box Fresh(out Option<Box> o) { o = Some(new Box(8)); return new Box(1); }\npublic sealed class Box(int v) { public int V = v; }\n", "VIB003")]
    [InlineData("Option<Box> a = Some(new Box(1));\nOption<Box> b = Some(new Box(2));\nvar m = a.AsMut();\nm.Set(Pick(Fresh(out m, b.AsMut())));\nWriteLine(a.Unwrap().V);\nstatic bool Fresh(out OptionMut<Box> dst, OptionMut<Box> src) { dst = src; return true; }\nstatic Option<Box> Pick(bool f) => f ? Some(new Box(9)) : None;\npublic sealed class Box(int v) { public int V = v; }\n", "VIB000")]
    [InlineData("Option<Box> a = Some(new Box(1));\nOption<Box> b = Some(new Box(2));\nvar m = a.AsMut();\nvar old = m.Replace(Pick(Fresh(out m, b.AsMut())));\nWriteLine(old.IsSome);\nstatic bool Fresh(out OptionMut<Box> dst, OptionMut<Box> src) { dst = src; return true; }\nstatic Option<Box> Pick(bool f) => f ? Some(new Box(9)) : None;\npublic sealed class Box(int v) { public int V = v; }\n", "VIB000")]
    [InlineData("Option<Box> a = Some(new Box(1));\nOption<Box> b = Some(new Box(2));\nvar m = a.AsMut();\nm.Set(Pick((m = b.AsMut()) is Some<Box>));\nWriteLine(a.Unwrap().V);\nstatic Option<Box> Pick(bool f) => f ? Some(new Box(9)) : None;\npublic sealed class Box(int v) { public int V = v; }\n", "VIB001")]
    public async Task D1WAndCursorRepointRejectWithDecidedIdSet(string source, string expected)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        Assert.Equal(new[] { expected }, result.Self);
    }

    [Theory]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Mix(Fresh(out o), o.IsSome)).V);\nstatic bool Fresh(out Option<Box> o) { o = Some(new Box(8)); return true; }\nstatic Box Mix(bool a, bool b) => new Box(a && b ? 1 : 2);\npublic sealed class Box(int v) { public int V = v; }\n", null)]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Mix((o = Some(new Box(8))).IsSome, Clear(ref o))).V);\nstatic bool Clear(ref Option<Box> o) { o = None; return true; }\nstatic Box Mix(bool a, bool b) => new Box(a && b ? 1 : 2);\npublic sealed class Box(int v) { public int V = v; }\n", null)]
    [InlineData("Option<Box> o = Some(new Box(5));\nWriteLine(o.UnwrapOr(Pick(o.AsMut().Take(), Fresh(out o))).V);\nstatic bool Fresh(out Option<Box> o) { o = Some(new Box(8)); return true; }\nstatic Box Pick(Option<Box> x, bool b) => x.UnwrapOr(new Box(b ? 1 : 2));\npublic sealed class Box(int v) { public int V = v; }\n", new[] { "VIB001", "VIB003" })]
    public async Task D1WMixedWriteAndUseRejects(string source, string[]? expectedExact)
    {
        var result = await Round12RuleWitness.CheckAllAsync(source);
        if (expectedExact is not null) Assert.Equal(expectedExact, result.Self);
        else Assert.NotEmpty(result.Self);
    }
}
