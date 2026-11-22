WriteLine(F(6, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += Us4(1) + Us4(4) * 10 + Us4(9) * 100;
    bool b5 = (p1 ? 3.25 : 9.25) >= (4.25 * 2.25);
    acc += Snd2(acc);
    return acc;
}
static int Snd2(int p) { int acc = 0; Option<Cell1> o = Some(new Cell1(p)); var s = o.AsRef(); var t = o.Take(); acc += t.IsSome ? 1 : 0; acc += (s is Some<Cell1>(var c) ? c.V : 0); return acc; }
static Result<Vec<int>, int> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
static int Us4(int p) { int n = 0; Result<Vec<int>, int> o = UsMk3(p); n += o switch { not Ok<Vec<int>> => 3, Ok<Vec<int>>(var v) when p == 4 => 50 + v.Count + v[0], Ok<Vec<int>>(var v) => v.Count + v[0] }; n += o.IsOk ? 1000 : 0; return n; }
public sealed class Cell1(int v) { public int V = v; }
