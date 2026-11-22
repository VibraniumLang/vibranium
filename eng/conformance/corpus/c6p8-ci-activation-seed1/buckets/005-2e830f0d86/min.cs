WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vm3(p0);
    acc += Us2(1) + Us2(4) * 10 + Us2(7) * 100;
    return 0;
}
static Result<Vec<int>, int> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
static int Us2(int p) { int n = 0; Result<Vec<int>, int> o = UsMk1(p); switch (o) { case not Ok<Vec<int>>: n += 3; break; case Ok<Vec<int>>(var v) when p == 4: n += 50 + v.Count + v[0]; break; case Ok<Vec<int>>(var v): n += v.Count + v[0]; break; } n += o.IsOk ? 1000 : 0; return n; }
static int Vm3(int p) { Vec<int> b = [p, 2, 3]; Span<int> s = b.AsSpan(); Vec<int> a = [4, 5]; int n = 0; Span<int> t = s; n += t.Length; s = a.AsSpan(); n += b[0]; n += s.Length; return n; }
