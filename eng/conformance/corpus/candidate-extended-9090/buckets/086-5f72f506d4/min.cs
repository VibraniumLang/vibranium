WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Span<int> s1 = [2, 3, 1, 7, 17, 18];
    foreach (ref var t2_e0 in s1) t2_e0 += 3;
    foreach (ref var t2_e1 in s1) t2_e1 += 2;
    Vec<int> v4 = [13, 20, -5, 18, 8];
    Span<int> s3 = v4[0..5];
    foreach (ref var t5_e1 in s3) t5_e1 += 1;
    acc += s3[3];
    return 0;
}
