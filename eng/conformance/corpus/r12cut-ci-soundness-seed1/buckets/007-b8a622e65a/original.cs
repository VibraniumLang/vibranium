WriteLine(F(2, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc = acc * 5;
    acc += M3(out acc, 4);
    WriteLine(acc);
    acc += Snd2(p0);
    return acc;
}
static int Snd2(int p) { int acc = 0; Option<Cell1> o = Some(new Cell1(p)); var s = o.AsMut(); for (int i = 0; i < p % 3; i++) { var w = o; acc += w.IsSome ? 1 : 0; } if (s is Some<Cell1>(var c)) { c.V += 1; acc += c.V; } return acc; }
static int M3(out int p, int k)
{
    p = k;
    return p * 2;
}
public sealed class Cell1(int v) { public int V = v; }
