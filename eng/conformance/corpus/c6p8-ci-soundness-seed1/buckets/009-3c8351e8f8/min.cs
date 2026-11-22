WriteLine(F(-1, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p0 is var q3 && q3 > 0)
    {
        acc += q3;
        int x6 = q3 + 0;
        acc += Us5(1) + Us5(3) * 10 + Us5(9) * 100;
    }
    acc += Snd2(p0);
    return acc;
}
static ReadOnlySpan<int> Head1(ReadOnlySpan<int> x) => x[..1];
static int Snd2(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; ReadOnlySpan<int> s = Head1(v.AsReadOnlySpan()); if (p > 1) { acc += s.Length; } else { v.Resize(8, p); } return acc; }
static Result<int, int> UsMk4(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
static int Us5(int p) { int n = 0; switch (UsMk4(p)) { case not Ok<int>: n += 3; break; case Ok<int>(var v) when p % 2 == 0: n += 50 + v; break; case Ok<int>(var v): n += v; break; } return n; }
