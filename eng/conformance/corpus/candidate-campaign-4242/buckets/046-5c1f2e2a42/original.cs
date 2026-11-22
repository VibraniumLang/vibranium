WriteLine(F(2, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    int _ = Find1(p0) switch { Some<int>(_) => p0, NoneType => p0, _ => _ };
    acc += 1;
    nint w3 = (nint)(acc + 3);
    WriteLine(w3 << 5);
    _ += 2;
    WriteLine(_);
    {
        int.TryParse("47", out int _);
    }
    WriteLine(_);
    _ += 2;
    WriteLine(_);
    return acc;
}
static Option<int> Find1(int k) => k != 1 ? Some(k * 2) : None;
