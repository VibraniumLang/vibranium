WriteLine(C.M(-2, true));
WriteLine(C.M(9, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Option<Cell1> o2 = None;
        var c3 = o2.Take();
        WriteLine(c3.IsSome);
        BumpO5(o2.AsMut(), 2);
        OptionMut<Cell1> mm7 = o2.AsMut();
        var d8 = (double)acc;
        var c6 = mm7.Take();
        if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
        int s9 = ((float)0L) % (3.5f % 9.5f) switch { 7.5f => p0, 6.5f when true => acc, _ => p0 };
        acc += s9;
        return acc;
    }
    public static void BumpO5(OptionMut<Cell1> m, int d) { if (m is Some<Cell1>(var c)) c.V += d; }
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
