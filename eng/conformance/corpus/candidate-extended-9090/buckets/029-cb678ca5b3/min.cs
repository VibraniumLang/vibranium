WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s2 = [5, 1, -5, 3];
    acc += SumRo4(s2);
    foreach (ref var t3_e1 in s2) t3_e1 += 3;
    Bump5(s2[1..^2], 1);
    static int L8(int a) => 0;
    acc += L8(0);
    return 0;
}
static int SumRo4(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
static void Bump5(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
