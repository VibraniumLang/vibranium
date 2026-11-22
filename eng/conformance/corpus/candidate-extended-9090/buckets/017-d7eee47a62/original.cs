WriteLine(F(-1, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Option<Cell1> o2 = None;
    acc += o2.AsRef() switch { Some<Cell1>(var c3) => c3.V, _ => -1 };
    var c4 = o2.Replace(Some(new Cell1(4)));
    if (c4.AsRef() is Some<Cell1>(var c4_p)) WriteLine(c4_p.V);
    const int K5 = 9;
    if (p1)
    {
        var d6 = (double)p2;
    }
    acc += M7(out acc, 2) + (p0 < 3 ? p1 ? p0 : p0 : 6);
    WriteLine(acc);
    switch (acc)
    {
        case K5: WriteLine(1); break;
        default: WriteLine(2); break;
    }
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    acc += p1 ? p0 / K5 : acc + 6;
    return acc;
}
static int M7(out int p, int k)
{
    p = k;
    return p * 2;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
