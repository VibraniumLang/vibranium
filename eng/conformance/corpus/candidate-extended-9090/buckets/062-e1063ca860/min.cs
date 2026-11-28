WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    return acc;
}
public sealed class C1(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        Vec<int> v3 = [3, 15, 18];
        Span<int> s2 = v3.Slice(0, 3);
        foreach (ref var t4_e1 in s2) t4_e1 += 2;
        WriteLine(SumRo5(s2));
        WriteLine(0 switch { < 0 => 0, >= 0 when true => 0 });
        return 0;
    }
    public static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
