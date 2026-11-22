WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Us2(2) + Us2(5) * 10 + Us2(7) * 100;
    return 0;
}
static Result<int, int> UsMk1(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
static int Us2(int p) { int n = 0; Result<int, int> o = UsMk1(p); switch (o) { case Ok<int>(var v) when p % 2 == 0: n += 50 + v; break; case var w: n += w.IsOk ? 300 : 400; break; } n += o.IsOk ? 1000 : 0; return n; }
