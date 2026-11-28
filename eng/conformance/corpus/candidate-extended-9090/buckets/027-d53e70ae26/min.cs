WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Span<int> s1 = [20, -1, -2, -2, 6];
        foreach (ref var t2_e3 in s1) t2_e3 += 2;
        Span<int> t2_w = s1[1..];
        s1[0] = 1;
        acc += t2_w[0];
        return 0;
    }
}
