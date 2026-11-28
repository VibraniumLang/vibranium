WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var _ = 0;
    long x1 = 0L;
    var s5 = Try2(0) switch { Ok<int>(var v3) => 0, Err<int>(var e4) => 0, _ => x1 switch { < 0L => 0, >= 0L when false => 0 } };
    WriteLine(_);
    return acc;
}
static Result<int, int> Try2(int k) => Ok(0);
