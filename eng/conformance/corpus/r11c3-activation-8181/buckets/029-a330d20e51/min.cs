WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Vw2(acc);
    var s7 = Try4(0) switch { _ => 0 };
    return 0;
}
static Span<int> First1(Span<int> x) => x;
static int Vw2(int p) { Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); Span<int> t = First1(s); t[0] = 4; s[1] = 3; return s.Length + v[0]; }
static Result<int, int> Try4(int k) => (Result<int, int>)Ok(0);
