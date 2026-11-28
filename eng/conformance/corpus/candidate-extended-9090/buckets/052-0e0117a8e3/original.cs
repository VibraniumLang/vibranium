WriteLine(F(4, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p0 is var q1)
    {
        acc += q1;
        Option<Cell2> o3 = Some(new Cell2(6));
        WriteLine(o3.IsSome);
        acc += o3.AsRef() switch { Some<Cell2>(var c5) => c5.V, _ => -1 };
        if (p1)
        {
            int x6 = (int)(p1 ? 'b' : 'd');
        }
        else
        {
            bool b7 = acc is > 2 and < 3;
        }
        if (o3.AsRef() is Some<Cell2>(var o3_f)) WriteLine(o3_f.V); else WriteLine(-1);
    }
    return acc;
}
public sealed class Cell2(int v)
{
    public int V = v;
    public Option<Cell2> Next = None;
}
