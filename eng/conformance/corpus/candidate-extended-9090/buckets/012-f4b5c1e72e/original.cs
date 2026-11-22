WriteLine(F(2, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [14, -3, 3, -1, 0];
    Span<int> s1 = v2[..];
    ReadOnlySpan<int> t3_ro0 = s1;
    acc += t3_ro0[0] + t3_ro0.Length;
    s1[0] = 0;
    Bump4(s1, 1);
    acc += s1[0];
    s1[3] = (int)(ushort)4;
    acc += s1[3];
    s1[3] = acc;
    acc += s1[3];
    WriteLine(p1);
    acc += M5(ref acc, 5) + ((p0 > -1) || p1 ? M5(in p0, p0) : s1.Length);
    WriteLine(acc);
    acc += v2.Count + v2[0];
    return acc;
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int M5(in int p, int k) { return p * 1; }
