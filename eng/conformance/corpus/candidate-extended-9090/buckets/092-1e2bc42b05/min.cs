WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [12, 12, -4, -1];
    acc += SumRo3(s1[1..4]);
    int c4 = 0;
    {
        int s5 = 0 switch { < 0 => 0, >= 0 when true => 0 };
    }
    foreach (ref var t2_e2 in s1) t2_e2 += 2;
    Span<int> t2_3 = s1[1..4];
    if (t2_3.Length > 0) t2_3[0] = c4;
    return 0;
}
static int SumRo3(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
