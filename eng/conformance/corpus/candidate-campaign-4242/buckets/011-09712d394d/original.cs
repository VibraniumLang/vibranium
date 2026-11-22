WriteLine(F(2, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var _ = -1;
    acc += _;
    _ *= 3;
    WriteLine(_);
    _ = _ * acc;
    WriteLine(_);
    int.TryParse("31", out _);
    WriteLine(_);
    acc += P1(_ is > 2 and < 3 ? None : Some(p0), -1);
    return acc;
}
static int P1(Option<int> c0, int c1) => c0.UnwrapOr(-1) + c1;
