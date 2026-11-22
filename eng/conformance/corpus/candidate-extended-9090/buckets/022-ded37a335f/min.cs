WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    ReadOnlySpan<byte> u2 = "tail\r"u8;
    foreach (var l1 in u2.Lines())
    {
        Span<int> s4 = [14, 11, 9, 5, 1];
        Bump6(s4[0..], 3);
        foreach (ref var t5_e1 in s4) t5_e1 += 2;
        ReadOnlySpan<int> t5_ro2 = s4;
    }
    return acc;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
