WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = 0;
        Span<int> s1 = [2, 14, -5, 18, 14, 7];
        foreach (ref var t2_e1 in s1) t2_e1 += 3;
        ReadOnlySpan<int> t2_ro3 = s1;
        return acc;
    }
}
