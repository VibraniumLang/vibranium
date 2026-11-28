WriteLine(F(5, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    long x3 = (long)p0;
    bool ok5 = TryGet4((int)(5.25 % 8.25), out _);
    WriteLine(ok5);
    _ = int.TryParse("10", out _);
    bool ok6 = int.TryParse("56", out var _);
    WriteLine(ok6);
    acc += Vw2(2);
    acc += p0 + p0;
    return acc;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s.Length + a.Count; }
static bool TryGet4(int k, out int v)
{
    v = k * 2;
    return k > 0;
}
