WriteLine(F(4, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1(acc));
    var c3 = o2.Take();
    WriteLine(c3.IsSome);
    BumpO5(o2.AsMut(), 5);
    OptionMut<Cell1> mm7 = o2.AsMut();
    var c6 = mm7.Replace(Some(new Cell1(7)));
    BumpO9(o2.AsMut(), 2);
    acc += acc;
    acc += M10(in acc, 3);
    WriteLine(acc);
    acc += M11(ref acc, 4);
    WriteLine(acc);
    var x12 = o2.AsMut();
    WriteLine(o2.IsSome);
    x12.Set(None);
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    if (p1)
    {
        int x13 = acc + 7;
    }
    return acc;
}
static void BumpO5(OptionMut<Cell1> m, int d) { if (m is Some<Cell1>(var c)) c.V += d; }
static void BumpO9(OptionMut<Cell1> m, int d) { if (m is Some<Cell1>(var c)) c.V += d; }
static int M10(in int p, int k) { return p * 3; }
static int M11(in int p, int k) { return p * 1; }
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
