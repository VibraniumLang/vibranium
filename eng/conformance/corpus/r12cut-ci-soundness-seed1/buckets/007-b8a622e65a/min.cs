WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += M3(out acc, 0);
    acc += Snd2(p0);
    return 0;
}
static int Snd2(int p) { int acc = 0; Option<Cell1> o = Some(new Cell1(p)); var s = o.AsMut(); for (int i = 0; i < p % 3; i++) { var w = o; acc += w.IsSome ? 1 : 0; } if (s is Some<Cell1>(var c)) { c.V += 1; acc += c.V; } return acc; }
static int M3(out int p, int k)
{
    p = 0;
    return 0;
}
public sealed class Cell1(int v) { public int V = v; }
