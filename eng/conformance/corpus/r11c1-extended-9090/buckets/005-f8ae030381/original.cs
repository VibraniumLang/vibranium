WriteLine(F(3, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    uint x1 = p1 ? 3u : 9u * 1u;
    switch (x1)
    {
        case < 4u:
        case >= 4u when true: return acc;
        default:
            Span<int> s2 = [4, -2, 13, 1, 11];
            foreach (ref var t3_e0 in s2) t3_e0 += 3;
            acc += s2[^1] + s2[^3];
            ReadOnlySpan<int> t3_ro2 = s2;
            acc += t3_ro2[0] + t3_ro2.Length;
            s2[0] = 4;
            Bump4(s2[4..^0], 2);
            acc += s2[0];
            int x5 = p0;
            Span<int> t3_w = s2;
            acc += s2[0];
            return x5;
    }
    return acc;
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
