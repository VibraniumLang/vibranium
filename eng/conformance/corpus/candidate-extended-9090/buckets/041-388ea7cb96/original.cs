WriteLine(C.M(4, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [8, 18, 1, 5, 19, -4];
        Span<int> s1 = v2.AsSpan();
        acc += s1[^1] + s1[^1];
        foreach (ref var t3_e1 in s1) t3_e1 += 2;
        S s4;
        if ((0.25 % 0.25) <= 1.25)
        {
            s4.A = s1.Length;
            s4.B = acc;
        }
        else
        {
            s4.A = acc;
            s4.B = acc;
        }
        Vec<int> v6 = [11, 11, 0, 17, 8, 3];
        Span<int> s5 = v6.Slice(0, 6);
        ReadOnlySpan<int> t7_ro0 = s5;
        acc += t7_ro0[0] + t7_ro0.Length;
        s5[0] = 5;
        s5[5] = acc / acc;
        acc += s5[5];
        var d8 = (double)p0;
        acc += v6.Count + v6[0];
        acc += Sum9(s4);
        acc += Over10(s1, s1);
        acc += v2.Count + v2[0];
        long x11 = 6L * (p1 ? -3L : 6L);
        return acc;
    }
    public static int Sum9(S x) => x.A + x.B;
    public static int Over10(Span<int> a, Span<int> b) => a.Length + b.Length;
}
public struct S
{
    public int A;
    public int B;
}
