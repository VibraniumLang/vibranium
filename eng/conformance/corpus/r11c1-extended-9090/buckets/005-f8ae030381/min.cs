WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    uint x1 = 0u;
    switch (x1)
    {
        case >= 0u: return 0;
        default:
            Span<int> s2 = [4, -2, 13, 1, 11];
            Bump4(s2[4..^0], 2);
            int x5;
            Span<int> t3_w = s2;
            acc += s2[0];
            return x5;
    }
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
