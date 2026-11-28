WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Vec<int> v7 = [3, 2, 13, 19, 8];
    Span<int> s6 = v7.AsSpan();
    Bump9(s6[3..^2], 3);
    ushort x10 = (ushort)0;
    var s11 = 0f + 0f switch { _ => 0 };
    acc += s11;
    {
        ushort x13 = x10;
    }
    return 0;
}
static void Bump9(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
