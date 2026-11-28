WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    static int L1(int _) { return 0; }
    acc += L1(0);
    Vec<int> v4 = [3, -5, 12, 3, 10];
    Span<int> s3 = v4.Slice(0, 5);
    foreach (ref var t5_e0 in s3) t5_e0 += 1;
    ReadOnlySpan<int> t5_ro3 = s3;
    return 0;
}
