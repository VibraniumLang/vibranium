WriteLine(C.M(0, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [20, 17, -2, -4, 6, -2];
        Span<int> s1 = v2.AsSpan();
        foreach (ref var t3_e0 in s1) t3_e0 += 2;
        Span<int> t3_1 = s1[2..3];
        var x4 = acc + 3;
        acc += First5([7, 3, x4]);
        acc += t3_1.Length;
        s1[4] += p0;
        acc += s1[4];
        acc += SumRo6(s1);
        acc += v2.Count + v2[0];
        acc += (int)((char)p0);
        return acc;
    }
    public static int First5(scoped ReadOnlySpan<int> q) => q.Length > 0 ? q[0] : -1;
    public static int SumRo6(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
