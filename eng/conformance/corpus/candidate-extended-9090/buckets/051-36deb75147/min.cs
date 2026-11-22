WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Option<Cell1> o2 = Some(new Cell1(acc << (acc - 1)));
    acc += Peek4(o2.AsRef());
    switch (0)
    {
        case >= 0: acc += M11(ref acc, 2) + (false ? 0 : 0); break;
    }
    return acc;
}
static int Peek4(OptionRef<Cell1> r) => r is Some<Cell1>(var c) ? c.V : -1;
static int M11(ref int p, int k)
{
    p += k;
    return 0;
}
public sealed class Cell1(int v)
{
    public int V = v;
}
