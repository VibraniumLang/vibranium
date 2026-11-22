WriteLine(F(6, true, -2L));
WriteLine(F(1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    C1 c3 = new C1(p0);
    acc += c3.Run();
    acc = p0;
    return acc;
}
public sealed class C1(int seed)
{
    public int _ = seed;
    public int Run()
    {
        int x2 = _ + 0;
        WriteLine($"v={x2}");
        _ = (x2 < 5 ? x2 : _) * (_ << 1);
        return _;
    }
}
