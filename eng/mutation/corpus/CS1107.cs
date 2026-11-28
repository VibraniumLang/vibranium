WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v2 = [20, 17, -2, -4, 6, -2];
        Span<int> s1 = v2.AsSpan();
        foreach (ref var t3_e0 in s1) t3_e0 += 2;
        Span<int> t3_1 = s1[2..3];
        var x4 = 0;
        acc += First5([x4]);
        return 0;
    }
    public static int First5(scoped scoped ReadOnlySpan<int> q) => 0;
}
