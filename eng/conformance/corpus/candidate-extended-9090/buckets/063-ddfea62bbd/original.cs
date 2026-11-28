WriteLine(F(5, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [5, acc];
    foreach (var e1 in v2)
    {
        var m3 = new IntMap<int>(0);
        _ = m3.Set(7, p0 + 6);
        _ = m3.Set(-3, p0);
        _ = m3.Set(-3, p0);
        _ = m3.Set(3, 8 - p0);
        for (int m3_i = 0; m3_i < 40; m3_i++) m3.Set(m3_i * 7, m3_i);
        int x4 = v2.Length;
        if (m3.Get(-2) is Some<int>(var x5)) WriteLine(x5); else WriteLine(-1);
        WriteLine(m3.Count);
        WriteLine(m3.Contains(4));
        var m3_m2 = m3;
        _ = m3.Set(1, 1);
        acc += e1;
    }
    return acc;
}
