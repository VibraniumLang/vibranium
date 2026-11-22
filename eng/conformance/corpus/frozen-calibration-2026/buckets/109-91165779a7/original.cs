WriteLine(F(6, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var _ = p0;
    long x1 = (long)_;
    var s5 = Try2(_) switch { Ok<int>(var v3) => acc, Err<int>(var e4) => 0 / acc, _ => x1 switch { < 6L => _, >= 6L when false => acc } };
    acc += s5;
    {
        int.TryParse("54", out _);
    }
    WriteLine(_);
    return acc;
}
static Result<int, int> Try2(int k) => k < 1 ? Ok(k) : Err(0 - k);
