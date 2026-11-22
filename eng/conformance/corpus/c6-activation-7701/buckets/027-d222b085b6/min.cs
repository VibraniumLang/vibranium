WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Vw4(p0);
    acc += Vw6(acc);
    acc += Vw2((p0 / p0) * (p1 ? -1 : 2));
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s.Length + a.Count; }
static Span<int> First3(Span<int> x) => x[..2];
static int Vw4(int p) { Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); Span<int> t = First3(s); t[0] = 4; return s.Length + v[0]; }
[return: LifetimeOf("x")] static ReadOnlySpan<int> Pick5(ReadOnlySpan<int> x, ReadOnlySpan<int> y, bool f) => f ? x : y;
static int Vw6(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4]; var s = Pick5(a.AsReadOnlySpan(), b.AsReadOnlySpan(), p > 0); return s[0]; }
