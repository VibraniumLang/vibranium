WriteLine(F(1, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Option<Cell1> o2 = None;
    var c3 = o2.Replace(Some(new Cell1(3)));
    if (c3.AsRef() is Some<Cell1>(var c3_p)) WriteLine(c3_p.V);
    if (o2.AsMut() is Some<Cell1>(var c4)) c4.V += 4;
    OptionMut<Cell1> mm6 = o2.AsMut();
    Vec<int> v8 = [-3];
    foreach (ref var e7 in v8)
    {
        e7 += 1;
        e7 += 4;
        var s9 = ((double)e7) - (5.25 * 5.25) switch { < 7.25 when false => (p0 / acc) / 7, _ => acc };
        acc += s9;
        acc += e7;
    }
    var c5 = mm6.Replace(None);
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return acc;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
