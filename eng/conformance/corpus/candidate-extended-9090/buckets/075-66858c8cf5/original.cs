WriteLine(F(6, true, -2L));
WriteLine(F(4, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1(acc));
    WriteLine(o2.IsSome);
    acc += Peek5(o2.AsRef());
    var u8 = Try6(8 * p0);
    if (u8 is Ok<int>(var v7))
    {
        acc += v7;
        static int L9(int a)
        {
            if (a == 1)
            {
                bool b10 = a != 2;
            }
            return a | 6;
        }
        var x13 = acc;
        acc += L9(p0);
        acc += L11(8 << v7);
        static int L11(int a)
        {
            long x12 = (long)a;
            return a <= 0 ? a >> a : a + L11(a - 1);
        }
    }
    else WriteLine(0);
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return acc;
}
static int Peek5(OptionRef<Cell1> r) => r is Some<Cell1>(var c) ? c.V : -1;
static Result<int, int> Try6(int k) => k != 2 ? (Result<int, int>)Ok(k) : Err(3 - k);
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
