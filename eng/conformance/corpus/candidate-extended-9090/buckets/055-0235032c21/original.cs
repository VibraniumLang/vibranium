WriteLine(F(0, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1(p0));
    var c3 = o2.Take();
    WriteLine(c3.IsSome);
    if (o2.AsRef() is Some<Cell1>(var c4)) acc += c4.V;
    var c5 = o2.Take();
    WriteLine(c5.IsSome);
    WriteLine(3u);
    acc += M6(ref acc, 5) + (p1 ? acc * p0 : p1 ? p0 : -2);
    WriteLine(acc);
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return acc;
}
static int M6(ref int p, int k)
{
    p += k;
    return p * 3;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
