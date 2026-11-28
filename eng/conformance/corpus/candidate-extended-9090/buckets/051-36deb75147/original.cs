WriteLine(F(6, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1(acc << (acc - 1)));
    acc += Peek4(o2.AsRef());
    OptionMut<Cell1> mm6 = o2.AsMut();
    var c5 = mm6.Take();
    OptionMut<Cell1> mm8 = o2.AsMut();
    var c7 = mm8.Replace(None);
    switch (acc)
    {
        case < 1: break;
        case >= 1:
            if (1 >= (p1 ? acc : acc))
            {
                bool b9 = p1;
            }
            else
            {
                var x10 = p1 ? 9L + -2L : 9L + 5L;
            }
            acc += M11(ref acc, 2) + (p1 ? acc : (int)'b');
            WriteLine(acc);
            break;
    }
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return acc;
}
static int Peek4(OptionRef<Cell1> r) => r is Some<Cell1>(var c) ? c.V : -1;
static int M11(ref int p, int k)
{
    p += k;
    return p * 1;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
