WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [2, 14, -1, 14, -4, 3];
    Span<int> s1 = v2.AsSpan();
    foreach (ref var t3_e1 in s1) t3_e1 += 1;
    ReadOnlySpan<int> t3_ro2 = s1;
    acc += M4(ref acc, 0);
    return 0;
}
static int M4(in int p, int k) { return 0; }
