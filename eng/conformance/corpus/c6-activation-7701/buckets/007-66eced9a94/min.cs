WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vw2(acc);
    const bool K3 = false;
    static int L4(int a) => K3 && a > 0 ? 0 : 0;
    acc += L4(0);
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
