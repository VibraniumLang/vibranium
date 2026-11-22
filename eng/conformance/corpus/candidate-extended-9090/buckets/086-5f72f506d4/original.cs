WriteLine(F(0, true, -2L));
WriteLine(F(-1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Span<int> s1 = [2, 3, 1, 7, 17, 18];
    foreach (ref var t2_e0 in s1) t2_e0 += 3;
    foreach (ref var t2_e1 in s1) t2_e1 += 2;
    Vec<int> v4 = [13, 20, -5, 18, 8];
    Span<int> s3 = v4[0..5];
    ReadOnlySpan<int> t5_ro0 = s3;
    acc += t5_ro0[0] + t5_ro0.Length;
    s3[0] = 5;
    foreach (ref var t5_e1 in s3) t5_e1 += 1;
    s3[3] += p0;
    acc += s3[3];
    acc += acc;
    if (!((ushort)3 == (ushort)3)) VibraniumRuntime.Panic("boom");
    acc += v4.Count + v4[0];
    long x6 = (long)p2;
    return acc;
}
