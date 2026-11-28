WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Option<Cell1> o2 = Some(new Cell1((acc / 7) & acc));
    Span<int> v6 = [];
    foreach (var e5 in v6)
    {
        S s7;
        s7.A = 0;
        s7.B = 0;
        var t8 = s7;
        t8.A = v6.Length;
    }
    return 0;
}
public sealed class Cell1(int v)
{
    public int V = v;
}
public struct S
{
    public int A;
    public int B;
}
