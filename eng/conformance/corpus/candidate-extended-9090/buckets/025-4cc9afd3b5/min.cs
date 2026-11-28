WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = 0;
        Vec<int> v2 = [2, -3, -3, 16];
        Span<int> s1 = v2[..];
        static int L5(int a) => 0;
        acc += L5(0);
        foreach (ref var t3_e2 in s1) t3_e2 += 1;
        acc += Over7(s1, s1);
        return 0;
    }
    public static int Over7(Span<int> a, Span<int> b) => a.Length + b.Length;
}
