WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    {
        acc += Vw4(2);
        static int L1(int a) { return 0; }
        acc += L1(0);
    }
    return 0;
}
static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
