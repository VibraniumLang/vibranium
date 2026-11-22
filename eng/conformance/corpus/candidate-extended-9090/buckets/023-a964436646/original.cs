WriteLine(C.M(-1, true));
WriteLine(C.M(8, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [20, 5, 11, 0, 2, 14];
        Span<int> s1 = v2.AsSpan();
        s1[1] += 2;
        acc += s1[1];
        acc += SumRo4(s1[1..]);
        foreach (ref var t3_e2 in s1) t3_e2 += 2;
        long x5 = (long)p0;
        int s6 = acc switch { 4 => acc, 1 => acc, int _ => acc };
        acc += s6;
        Span<int> t3_w = s1[1..];
        s1[0] = 1;
        acc += t3_w[0];
        acc += v2.Count + v2[0];
        return acc;
    }
    public static int SumRo4(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
