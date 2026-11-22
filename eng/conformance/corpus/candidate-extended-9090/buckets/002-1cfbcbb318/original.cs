WriteLine(F(-1, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    int x1 = p0;
    switch (x1)
    {
        case < 3:
            Vec<int> v3 = [4, 9, 12, 8, 20, -4];
            Span<int> s2 = v3[0..6];
            foreach (ref var t4_e0 in s2) t4_e0 += 1;
            acc += SumRo5(s2[1..7]);
            ReadOnlySpan<int> t4_ro2 = s2;
            acc += t4_ro2[0] + t4_ro2.Length;
            s2[0] = 0;
            x1 += x1;
            acc += v3.Count + v3[0];
            int.TryParse("9", out var o6);
            acc += o6;
            break;
        case >= 3 when false: o6 = -1; acc += o6; break;
    }
    return acc;
}
static int SumRo5(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
