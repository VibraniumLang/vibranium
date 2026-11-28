WriteLine(F(6, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var sm1 = new StrMap<int>(8);
    _ = sm1.Set("bb", p0);
    _ = sm1.Set("a\n"u8, acc);
    _ = sm1.Set("a\n", (ushort)3 != (ushort)4 ? acc : (int)(byte)4);
    _ = sm1.Set("a", acc);
    WriteLine(p1);
    int s2 = acc switch { < 4 => 6, >= 4 when (ushort)6 != (ushort)0 => acc * 4 };
    acc += s2;
    Span<int> sp3 = [acc, 4, 6];
    foreach (var e4 in sp3)
    {
        acc += e4;
    }
    acc += sm1.Get("a").UnwrapOr(-1);
    WriteLine(sm1.Count);
    int x5 = p0 is 4 ? 0 : p0;
    return acc;
}
