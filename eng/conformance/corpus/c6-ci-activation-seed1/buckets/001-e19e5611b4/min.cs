WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vw6((p0 * acc) % (acc * p0));
    acc += Ct3(acc);
    return 0;
}
static Vec<int> MakeV1() => [1, 2, 3];
static int Len2(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct3(int p) { Vec<int> v = [p]; bool c = p > 0; return Len2(c ? v.AsReadOnlySpan() : MakeV1().AsReadOnlySpan()) + Len2(!c ? v.AsReadOnlySpan() : MakeV1().AsReadOnlySpan()); }
static ReadOnlySpan<int> Sx5(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw6(int p) { Vec<int> a = [p, 5, 6]; var s = Sx5(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
