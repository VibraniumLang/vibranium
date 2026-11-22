WriteLine(F(2, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Option<int> x1 = Some(p0 - acc);
    switch (x1)
    {
        case Some<int>: return acc;
        case NoneType when acc is not 0: return 1;
        default:
            Vec<int> v4 = [18, 1, -5, 16, 13];
            Span<int> s3 = v4[0..5];
            ReadOnlySpan<int> t5_ro0 = s3;
            acc += t5_ro0[0] + t5_ro0.Length;
            s3[0] = 3;
            foreach (ref var t5_e1 in s3) t5_e1 += 3;
            ReadOnlySpan<int> t5_2 = s3[1..];
            acc += acc;
            acc += P6(p0 is 7 ? p2 + p2 : 1L + p2, acc);
            foreach (var t5_e2 in t5_2) acc += t5_e2;
            ReadOnlySpan<int> t5_ro3 = s3;
            acc += t5_ro3[0] + t5_ro3.Length;
            s3[0] = 7;
            acc += v4.Count + v4[0];
            int x7 = P6(p2, p0);
            return acc;
    }
    return acc;
}
static int P6(long c0, int c1) => (int)c0 + c1;
