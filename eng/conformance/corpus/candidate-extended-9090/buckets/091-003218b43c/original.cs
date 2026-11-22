WriteLine(F(6, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    int c1 = 0;
    do
    {
        c1 += 1;
        ReadOnlySpan<byte> u3 = "one\n\ntwo"u8;
        foreach (var l2 in u3.Lines()) { acc += l2.Length; WriteLine(l2.IndexOf((byte)'2')); }
        int x6 = acc;
        acc += L5(acc);
        static int L5(int a) => a <= 0 ? 2 : a + L5(a - 1);
    }
    while (c1 < 4);
    return acc;
}
