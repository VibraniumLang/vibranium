WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v2 = [8, 18, 1, 5, 19, -4];
        Span<int> s1 = v2.AsSpan();
        foreach (ref var t3_e1 in s1) t3_e1 += 2;
        S s4;
        s4.B = 0;
        acc += Over10(s1, s1);
        return 0;
    }
    public static int Over10(Span<int> a, Span<int> b) => a.Length + b.Length;
}
public struct S
{
    public int B;
}
