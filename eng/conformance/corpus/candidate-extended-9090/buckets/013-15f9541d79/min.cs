WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [16, 18, 11, 12, 17];
    foreach (ref var t2_e1 in s1) t2_e1 += 2;
    Bump3(s1[..^1], 2);
    var s4 = 0;
    acc += s4;
    return 0;
}
static void Bump3(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
