WriteLine(C.M(-1, true));
WriteLine(C.M(1, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Option<Cell1> o2 = None;
        acc += Peek4(o2.AsRef());
        WriteLine(o2.IsSome);
        WriteLine(o2.IsSome);
        if (o2.AsMut() is Some<Cell1>(var c7)) c7.V += 5;
        acc += acc;
        if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
        acc += M8(ref acc, 4) + (!p1 ? 1 : p0);
        WriteLine(acc);
        return acc;
    }
    public static int Peek4(OptionRef<Cell1> r) => r is Some<Cell1>(var c) ? c.V : -1;
    public static int M8(ref int p, int k)
    {
        p += k;
        return p * 1;
    }
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
