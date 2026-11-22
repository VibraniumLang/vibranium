WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v2 = [20, 5, 11, 0, 2, 14];
        Span<int> s1 = v2.AsSpan();
        foreach (ref var t3_e2 in s1) t3_e2 += 2;
        int s6 = 0;
        acc += s6;
        Span<int> t3_w = s1[1..];
        s1[0] = 1;
        acc += t3_w[0];
        return 0;
    }
}
