WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vw2(8);
    static int L3(int a) => 0;
    acc += L3(0);
    acc += L4(0);
    static int L4(int a) => 0;
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
