WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Result<int, int> u2 = Try1(0);
    Span<int> s3 = [7, 3, 17];
    foreach (ref var t4_e2 in s3) t4_e2 += 2;
    acc += s3[^1] + s3[^2];
    acc += 0 switch { < 0 when true => 0, >= 0 => 0 };
    return 0;
}
static Result<int, int> Try1(int k) => (Result<int, int>)Ok(0);
