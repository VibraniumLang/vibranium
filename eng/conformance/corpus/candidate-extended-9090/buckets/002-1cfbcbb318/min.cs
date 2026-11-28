WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    int x1 = 0;
    switch (0)
    {
        case < 3:
            Vec<int> v3 = [4, 9, 12, 8, 20, -4];
            Span<int> s2 = v3[0..6];
            foreach (ref var t4_e0 in s2) t4_e0 += 1;
            acc += SumRo5(s2[1..7]);
            x1 += 0;
            break;
    }
    return 0;
}
static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
