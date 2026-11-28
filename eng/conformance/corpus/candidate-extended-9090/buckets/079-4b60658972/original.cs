WriteLine(F(-1, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [20, 12, 4, 12, 3, 20];
    Span<int> s1 = v2.Slice(0, 6);
    foreach (ref var t3_e0 in s1) t3_e0 += 1;
    ReadOnlySpan<int> t3_1 = s1[..^0];
    Vec<int> v5 = [acc, acc, 0];
    foreach (var e4 in v5)
    {
        Vec<int> v7 = [8, 16, -5];
        Span<int> s6 = v7.Slice(0, 3);
        foreach (ref var t8_e0 in s6) t8_e0 += 3;
        Span<int> t8_1 = s6[2..];
        if (p0 is > 2 and < 3)
        {
            bool b9 = p1;
        }
        else
        {
            var d10 = (double)e4;
        }
        if (t8_1.Length > 0) t8_1[0] = 7;
        foreach (var t8_e1 in t8_1) acc += t8_e1;
        acc += v7.Count + v7[0];
        acc += e4;
        acc += e4;
    }
    acc += t3_1.Length;
    ReadOnlySpan<int> t3_ro2 = s1;
    acc += t3_ro2[0] + t3_ro2.Length;
    s1[0] = 9;
    acc += SumRo11(s1);
    acc += v2.Count + v2[0];
    return acc;
}
static int SumRo11(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
