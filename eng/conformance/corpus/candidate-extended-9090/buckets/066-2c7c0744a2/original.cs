WriteLine(F(1, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [2, 14, -1, 14, -4, 3];
    Span<int> s1 = v2.AsSpan();
    ReadOnlySpan<int> t3_ro0 = s1;
    acc += t3_ro0[0] + t3_ro0.Length;
    s1[0] = 2;
    foreach (ref var t3_e1 in s1) t3_e1 += 1;
    ReadOnlySpan<int> t3_ro2 = s1;
    acc += t3_ro2[0] + t3_ro2.Length;
    s1[0] = 0;
    acc += acc;
    acc += M4(ref acc, 3);
    WriteLine(acc);
    acc += v2.Count + v2[0];
    return acc;
}
static int M4(in int p, int k) { return p * 2; }
