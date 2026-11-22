WriteLine(F(2, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Result<int, int> u2 = Try1(p1 ? p0 : p0);
    Span<int> s3 = [7, 3, 17];
    ReadOnlySpan<int> t4_r0 = s3.Slice(1);
    acc += t4_r0.Length + (t4_r0.IsEmpty ? 0 : t4_r0[^1]);
    acc += s3[^1] + s3[^1];
    foreach (ref var t4_e2 in s3) t4_e2 += 2;
    acc += s3[^1] + s3[^2];
    if (p1)
    {
        double d5 = (double)acc;
    }
    acc += acc switch { < 2 when true => -1, >= 2 => acc };
    return acc;
}
static Result<int, int> Try1(int k) => k != 4 ? (Result<int, int>)Ok(k) : Err(0 - k);
