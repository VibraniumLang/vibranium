WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [7, 16, 11, 5];
    Span<int> s1 = v2.AsSpan();
    foreach (ref var t3_e0 in s1) t3_e0 += 1;
    acc += Pair4(s1[..0], s1[0..]);
    return 0;
}
static int Pair4(Span<int> a, Span<int> b) { if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }
