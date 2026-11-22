WriteLine(C.M(5, true, -2L));
WriteLine(C.M(6, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Span<int> v2 = [8, p0, acc];
        foreach (var e1 in v2)
        {
            Option<Cell3> o4 = Some(new Cell3(e1));
            if (o4.AsMut() is Some<Cell3>(var c5)) c5.Next = Some(new Cell3(8));
            if (o4.AsRef() is Some<Cell3>(var c5_a) && c5_a.Next.AsRef() is Some<Cell3>(var c5_n)) WriteLine(c5_n.V);
            if (o4.AsMut() is Some<Cell3>(var c6)) c6.V += 1;
            acc -= e1 / -3;
            if (o4.AsRef() is Some<Cell3>(var o4_f)) WriteLine(o4_f.V); else WriteLine(-1);
            acc += v2.Length + acc;
            acc += e1;
        }
        return acc;
    }
}
public sealed class Cell3(int v)
{
    public int V = v;
    public Option<Cell3> Next = None;
}
