WriteLine(C.M(-2, true));
WriteLine(C.M(0, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        for (int i1 = 0; i1 < 1; i1 += 1)
        {
            Span<int> s2 = [5, 15, 14, 10, 15];
            foreach (ref var t3_e0 in s2) t3_e0 += 3;
            acc += s2[^1] + s2[^5];
            Bump4(s2, 2);
            acc += s2[0];
            if (p1)
            {
                var d5 = (double)acc;
            }
        }
        acc *= acc;
        return acc;
    }
    public static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
}
