WriteLine(F(5, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    ReadOnlySpan<byte> u2 = "a\nb"u8;
    foreach (var l1 in u2.Lines())
    {
        WriteLine(l1.SequenceEqual("a"));
        WriteLine(l1.StartsWith("k"));
        acc += l1.Length;
        Span<int> v5 = [8, acc];
        foreach (var e4 in v5)
        {
            static int L6(int a)
            {
                if (((ushort)4 >= (ushort)1) && ((ushort)0 < (ushort)7))
                {
                    int x7 = a + 4;
                }
                else
                {
                    ushort x8 = (ushort)(a);
                }
                return a + 2;
            }
            acc = (p0 % p0) + v5.Length;
            acc += L6(p0);
            if (p1) break;
            if (!p1)
            {
                bool b9 = p2 == 1L;
            }
            acc += e4;
        }
    }
    return acc;
}
