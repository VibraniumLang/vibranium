WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    int c1 = 0;
    do
    {
        ReadOnlySpan<byte> u3 = "one\n\ntwo"u8;
        foreach (var l2 in u3.Lines()) { acc += l2.Length; WriteLine(l2.IndexOf((byte)'2')); }
        acc += L5(0);
        static int L5(int a) => L5(0);
    }
    while (c1 < 0);
    return 0;
}
