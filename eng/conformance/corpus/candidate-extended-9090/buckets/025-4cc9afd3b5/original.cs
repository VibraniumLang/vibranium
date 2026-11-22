WriteLine(C.M(0, true, -2L));
WriteLine(C.M(1, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Vec<int> v2 = [2, -3, -3, 16];
        Span<int> s1 = v2[..];
        Span<int> t3_0 = s1[0..3];
        WriteLine(7);
        static int L5(int a) => a + (int)E.C;
        acc += L5(acc);
        foreach (var t3_e0 in t3_0) acc += t3_e0;
        Bump6(s1[0..3], 3);
        acc += s1[0];
        foreach (ref var t3_e2 in s1) t3_e2 += 1;
        acc += Over7(s1, s1);
        acc += v2.Count + v2[0];
        return acc;
    }
    public static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
    public static int Over7(Span<int> a, Span<int> b) => a.Length + b.Length;
}
public enum E { A = 2, B = 3, C = 6 }
