WriteLine(F(-2, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    static int L1(int _)
    {
        WriteLine(_);
        {
            int.TryParse("36", out var _);
        }
        WriteLine(_);
        _ = _ + _;
        WriteLine(_);
        int x2 = _;
        return _;
    }
    acc += L1(p0 - (p1 ? acc : p0));
    Vec<int> v4 = [3, -5, 12, 3, 10];
    Span<int> s3 = v4.Slice(0, 5);
    foreach (ref var t5_e0 in s3) t5_e0 += 1;
    s3[0] = p0;
    acc += s3[0];
    acc += s3[^1] + s3[^3];
    ReadOnlySpan<int> t5_ro3 = s3;
    acc += t5_ro3[0] + t5_ro3.Length;
    s3[0] = 9;
    acc += 8;
    acc += v4.Count + v4[0];
    return acc;
}
