WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    ushort x1 = (ushort)0;
    int r2;
    switch (x1)
    {
        case >= (ushort)0: r2 = 0; break;
        default: Option<Cell3> o4 = Some(new Cell3(2)); acc += Two10(o4.AsMut(), o4.AsMut());
    }
    acc += r2;
    return 0;
}
static int Two10(OptionMut<Cell3> a, OptionMut<Cell3> b) => a.IsSome ? 1 : 0;
public sealed class Cell3(int v)
{
    public int V = v;
}
