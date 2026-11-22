WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += M6(ref acc, 5) + (false ? 0 : 0);
    return acc;
}
static int M6(ref int p, int k)
{
    p += k;
    return 0;
}
public sealed class Cell1(int v)
{
    public int V = v;
}
