WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [15, 2, 18, -3];
    foreach (ref var t2_e2 in s1) t2_e2 += 1;
    acc += Pair3(s1[..4], s1[4..]);
    return 0;
}
static int Pair3(Span<int> a, Span<int> b) { if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }
