WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Option<Cell1> o2 = Some(new Cell1(acc));
    acc += Peek5(o2.AsRef());
    var u8 = Try6(0);
    if (u8 is Ok<int>(var v7))
    {
        static int L9(int a) { return 0; }
        acc += L9(0);
        acc += L11(8 << v7);
        static int L11(int a) { return L11(a); }
    }
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return 0;
}
static int Peek5(OptionRef<Cell1> r) => r is Some<Cell1>(var c) ? c.V : -1;
static Result<int, int> Try6(int k) => (Result<int, int>)Ok(0);
public sealed class Cell1(int v)
{
    public int V = v;
}
