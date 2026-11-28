WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = 0;
        Vec<int> v2 = [14, 12, 15, 10, 13, -5];
        Span<int> s1 = v2.AsSpan();
        var s4 = 0 switch { < 0 => 0, >= 0 when true => 0 };
        foreach (ref var t3_e2 in s1) t3_e2 += 3;
        acc += Over6(s1[0..2], s1[1..3]);
        return 0;
    }
    public static int Over6(Span<int> a, Span<int> b) => a.Length + b.Length;
}
