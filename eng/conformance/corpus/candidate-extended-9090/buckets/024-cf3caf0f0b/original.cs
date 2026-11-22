WriteLine(F(1, true, -2L));
WriteLine(F(9, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    ReadOnlySpan<byte> tx1 = " 12 "u8;
    WriteLine(tx1.Trim());
    WriteLine(tx1.StartsWith("a"));
    WriteLine(tx1.IndexOf((byte)' '));
    if (long.TryParse("-7", out var pv5)) WriteLine(pv5); else WriteLine(0);
    Vec<int> v7 = [9, -5, 12, 14];
    Span<int> s6 = v7.AsSpan();
    ReadOnlySpan<int> t8_ro0 = s6;
    acc += t8_ro0[0] + t8_ro0.Length;
    s6[0] = 7;
    foreach (ref var t8_e1 in s6) t8_e1 += 2;
    Bump9(s6[1..6], 4);
    acc += s6[0];
    acc += s6[^1] + s6[^2];
    WriteLine(8u * 4u);
    acc += v7.Count + v7[0];
    return acc;
}
static void Bump9(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
