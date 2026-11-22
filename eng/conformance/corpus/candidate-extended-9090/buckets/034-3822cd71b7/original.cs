WriteLine(C.M(0, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [8, -1, 18, -3, 6, 14];
        Span<int> s1 = v2[..];
        foreach (ref var t3_e0 in s1) t3_e0 += 2;
        acc += s1[^1] + s1[^5];
        int x4 = acc + (int)(-2L + -1L);
        acc += v2.Count + v2[0];
        int s5 = x4 switch { < 0 when true => p0, >= 0 => p0 };
        acc += s5;
        return acc;
    }
}
