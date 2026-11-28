WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Ct5(acc * acc);
    acc += Vw2(-1);
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
static Vec<int> MakeV3() => [1, 2, 3];
static int Len4(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct5(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV3().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len4(v.AsReadOnlySpan()); }
