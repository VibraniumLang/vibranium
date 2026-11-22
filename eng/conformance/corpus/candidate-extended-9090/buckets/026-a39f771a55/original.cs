WriteLine(F(6, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    ushort x1 = (ushort)(p0 + -2);
    int r2;
    switch (x1)
    {
        case < (ushort)2: r2 = acc; break;
        case >= (ushort)2: r2 = -3; break;
        default:
            Option<Cell3> o4 = Some(new Cell3(2));
            OptionMut<Cell3> mm6 = o4.AsMut();
            var c5 = mm6.Take();
            var c7 = o4.Replace(Some(new Cell3(3)));
            if (c7.AsRef() is Some<Cell3>(var c7_p)) WriteLine(c7_p.V);
            int x8 = 5 >> p0;
            acc += Two10(o4.AsMut(), o4.AsMut());
            if (o4.AsRef() is Some<Cell3>(var o4_f)) WriteLine(o4_f.V); else WriteLine(-1);
            r2 = 7;
    }
    acc += r2;
    acc += (int)(6L % 2L);
    return acc;
}
static int Two10(OptionMut<Cell3> a, OptionMut<Cell3> b) => a.IsSome ? 1 : 0;
public sealed class Cell3(int v)
{
    public int V = v;
    public Option<Cell3> Next = None;
}
