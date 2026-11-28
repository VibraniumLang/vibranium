WriteLine(F(3, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    WriteLine(unchecked((nint)(1.8446744073709552e19)));
    WriteLine(unchecked((nint)(-1e30)));
    acc += Vw2(acc);
    int x3 = p0 + 5;
    var s7 = Try4(-1) switch { Ok<int> => -2, _ => 2 };
    acc += s7;
    return acc;
}
static Span<int> First1(Span<int> x) => x;
static int Vw2(int p) { Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); Span<int> t = First1(s); t[0] = 4; s[1] = 3; return s.Length + v[0]; }
static Result<int, int> Try4(int k) => k != 0 ? (Result<int, int>)Ok(k) : Err(2 - k);
