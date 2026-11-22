WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [12, -5, 6, 14];
    Span<int> s1 = v2.Slice(0, 4);
    foreach (ref var t3_e1 in s1) t3_e1 += 3;
    C4 c5 = new C4(s1.Length);
    return acc;
}
public sealed class C4(int seed)
{
    public int F = seed;
    public int Run()
    {
        F += 0;
        return 0;
    }
}
