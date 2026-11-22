WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Option<Cell1> o2 = Some(new Cell1(acc));
    BumpO5(o2.AsMut(), 5);
    BumpO9(o2.AsMut(), 2);
    acc += M10(in acc, 0);
    acc += M11(ref acc, 0);
    var x12 = o2.AsMut();
    WriteLine(o2.IsSome);
    x12.Set(None);
    return 0;
}
static void BumpO5(OptionMut<Cell1> m, int d) { if (m is Some<Cell1>(var c)) c.V += d; }
static void BumpO9(OptionMut<Cell1> m, int d) { if (m is Some<Cell1>(var c)) c.V += d; }
static int M10(in int p, int k) { return 0; }
static int M11(in int p, int k) { return 0; }
public sealed class Cell1(int v)
{
    public int V = v;
}
