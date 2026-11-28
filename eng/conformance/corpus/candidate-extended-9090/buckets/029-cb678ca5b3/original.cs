WriteLine(F(-2, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s2 = [5, 1, -5, 3];
    acc += SumRo4(s2);
    foreach (ref var t3_e1 in s2) t3_e1 += 3;
    Bump5(s2[1..^2], 1);
    acc += s2[0];
    if (((long)p0) <= (-3L + 0L))
    {
        double d6 = (double)p0;
    }
    else
    {
        ushort x7 = (ushort)(acc);
    }
    static int L8(int a) => a + (int)E.C;
    acc += L8(acc);
    return acc;
}
static int SumRo4(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
static void Bump5(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
public enum E { A = 2, B = 3, C = 7 }
