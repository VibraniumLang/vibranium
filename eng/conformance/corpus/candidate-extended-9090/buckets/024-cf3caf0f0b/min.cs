WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    ReadOnlySpan<byte> tx1 = " 12 "u8;
    WriteLine(tx1.IndexOf((byte)' '));
    Vec<int> v7 = [9, -5, 12, 14];
    Span<int> s6 = v7.AsSpan();
    foreach (ref var t8_e1 in s6) t8_e1 += 2;
    Bump9(s6[1..6], 4);
    return acc;
}
static void Bump9(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
