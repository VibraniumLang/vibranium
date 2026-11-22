WriteLine(C.M(4, true));
WriteLine(C.M(9, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [8, -5, 13, 20, 20, -2];
        Span<int> s1 = v2[..];
        acc += s1[^1] + s1[^2];
        acc += SumRo4(s1[0..]);
        acc += SumRo5(s1);
        if (!((ushort)5 < (ushort)1))
        {
            int x6 = p0;
        }
        acc += v2.Count + v2[0];
        nint w7 = (nint)(p0 + 5);
        w7 %= (nint)-2;
        WriteLine(w7);
        return acc;
    }
    public static int SumRo4(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
    public static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
