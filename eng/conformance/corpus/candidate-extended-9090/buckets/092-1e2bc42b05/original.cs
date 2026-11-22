WriteLine(F(0, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [12, 12, -4, -1];
    acc += SumRo3(s1[1..4]);
    Span<int> t2_1 = s1[1..4];
    int c4 = 0;
    do
    {
        c4 += 1;
        acc += c4;
        int s5 = acc switch { < 1 => acc, >= 1 when true => acc };
        acc += s5;
        if (4.25 > -3.25) continue;
    }
    while (c4 < 2);
    if (t2_1.Length > 0) t2_1[0] = -3;
    acc += t2_1.Length;
    foreach (ref var t2_e2 in s1) t2_e2 += 2;
    Span<int> t2_3 = s1[1..4];
    if (t2_3.Length > 0) t2_3[0] = c4;
    acc += t2_3.Length;
    acc += p0;
    return acc;
}
static int SumRo3(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
