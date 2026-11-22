WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    ReadOnlySpan<byte> u4 = "a\r\nb\r\n"u8;
    foreach (var l3 in u4.Lines())
    {
        Vec<int> v8 = [2, 9, 16, 5, -3];
        Span<int> s7 = v8.Slice(0, 5);
        foreach (ref var t9_e0 in s7) t9_e0 += 3;
        acc += Over10(s7, s7);
    }
    return 0;
}
static int Over10(Span<int> a, Span<int> b) => a.Length + b.Length;
