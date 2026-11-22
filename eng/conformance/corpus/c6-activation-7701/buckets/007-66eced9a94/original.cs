WriteLine(F(-2, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Vw2(acc);
    const bool K3 = false;
    WriteLine(2.25 > (p1 ? 8.25 : 3.25));
    static int L4(int a) => (a <= 0 ? a * 4 : a + L4(a - 1)) + ((K3 && a > 0) ? 1 : 2);
    acc += L4(p0 % 1000);
    return acc;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
