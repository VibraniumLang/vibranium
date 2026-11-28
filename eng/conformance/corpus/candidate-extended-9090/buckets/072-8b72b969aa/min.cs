WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc;
    ReadOnlySpan<byte> u2 = "a\nb"u8;
    foreach (var l1 in u2.Lines())
    {
        Span<int> v5 = [];
        foreach (var e4 in v5)
        {
            static int L6(int a) { return 0; }
            acc = v5.Length;
            acc += L6(0);
        }
    }
    return 0;
}
