WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case < 0: return 1;
        case >= 0: return 2;
        default:
            Option<Cell> o = Some(new Cell(2));
            return Two(o.AsMut(), o.AsMut());
    }
}
static int Two(OptionMut<Cell> a, OptionMut<Cell> b) => a.IsSome ? 1 : 0;
public sealed class Cell(int v) { public int V = v; }
