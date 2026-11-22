WriteLine(C.M(0, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<int> v2 = [-3, -5, 10, -2, 18];
        Span<int> s1 = v2[..];
        acc += Pair4(s1[..4], s1[4..]);
        foreach (ref var t3_e1 in s1) t3_e1 += 2;
        Bump5(s1, 3);
        acc += s1[0];
        Bump6(s1, 4);
        acc += s1[0];
        ushort x7 = (ushort)(acc);
        v2.Add(1);
        acc += s1[0];
        acc += v2.Count + v2[0];
        acc += M8(ref acc, 3) + (p1 ? p0 : -3);
        WriteLine(acc);
        return acc;
    }
    public static int Pair4(Span<int> a, Span<int> b) { if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }
    public static void Bump5(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
    public static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
    public static int M8(in int p, int k) { return p * 1; }
}
