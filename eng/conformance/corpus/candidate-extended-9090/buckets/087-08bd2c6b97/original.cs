WriteLine(C.M(0, true, -2L));
WriteLine(C.M(3, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Vec<int> v2 = [14, 12, 15, 10, 13, -5];
        Span<int> s1 = v2.AsSpan();
        Span<int> t3_0 = s1[1..3];
        acc += acc;
        var s4 = p0 switch { < 0 => (int)((byte)p2), >= 0 when true => acc };
        acc += s4;
        if (t3_0.Length > 0) t3_0[0] = p1 ? p0 : p0;
        foreach (var t3_e0 in t3_0) acc += t3_e0;
        acc += SumRo5(s1);
        foreach (ref var t3_e2 in s1) t3_e2 += 3;
        acc += Over6(s1[0..2], s1[1..3]);
        acc += v2.Count + v2[0];
        return acc;
    }
    public static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
    public static int Over6(Span<int> a, Span<int> b) => a.Length + b.Length;
}
