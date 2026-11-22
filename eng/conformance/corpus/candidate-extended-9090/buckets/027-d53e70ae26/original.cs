WriteLine(C.M(1, true));
WriteLine(C.M(4, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Span<int> s1 = [20, -1, -2, -2, 6];
        foreach (ref var t2_e0 in s1) t2_e0 += 1;
        ReadOnlySpan<int> t2_ro1 = s1;
        acc += t2_ro1[0] + t2_ro1.Length;
        s1[0] = 9;
        acc += SumRo3(s1[2..]);
        foreach (ref var t2_e3 in s1) t2_e3 += 2;
        WriteLine(((long)4L) + ((long)p0));
        ReadOnlySpan<char> sp4 = (8L == 8L) ? ['c'] : ['f'];
        acc += (int)sp4[0];
        Span<int> t2_w = s1[1..];
        s1[0] = 1;
        acc += t2_w[0];
        return acc;
    }
    public static int SumRo3(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
