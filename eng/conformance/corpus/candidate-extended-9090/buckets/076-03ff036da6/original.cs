WriteLine(C.M(4, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Option<Cell1> o2 = Some(new Cell1(p0));
        acc += o2.AsRef() switch { Some<Cell1>(var c3) => c3.V, _ => -1 };
        if (o2.AsMut() is Some<Cell1>(var c4)) c4.V += 5;
        S s5 = new S();
        WriteLine(p0);
        s5.B = 5;
        acc += Sum6(s5);
        acc += M7(out acc, 5) + (p1 ? acc % 5 : p0 ^ acc);
        WriteLine(acc);
        if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
        int x8 = p0;
        return acc;
    }
    public static int Sum6(in S x) => x.A + x.B;
    public static int M7(out int p, int k)
    {
        p = k;
        return p * 1;
    }
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
public struct S
{
    public int A;
    public int B;
}
