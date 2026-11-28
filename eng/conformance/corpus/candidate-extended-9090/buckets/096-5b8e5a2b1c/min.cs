WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Vec<int> v7 = [12, 9, 6, 3, 9];
    Span<int> s6 = v7.AsSpan();
    foreach (ref var t8_e1 in s6) t8_e1 += 2;
    ReadOnlySpan<int> t8_r2 = s6.Slice(1, 3);
    int s9 = 0 switch { < 0 when true => 0, >= 0 => 0 };
    return acc;
}
