WriteLine(F(6, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (acc is var q1 && q1 > 2)
    {
        acc += q1;
        acc += acc;
        acc += Us3(2) + Us3(4) * 10 + Us3(7) * 100;
    }
    var s4 = acc switch { 2 => acc, not 2 => acc - 6 };
    acc += s4;
    return acc;
}
static Result<int, int> UsMk2(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
static int Us3(int p) { int n = 0; Result<int, int> o = UsMk2(p); for (int i = 0; i < 2; i++) { switch (o) { case not Err<int> when (p + i) != 4: n += 100; continue; case not Err<int> when (p + i) >= 4: n += 30; continue; case Ok<int>(var v): n += v; break; default: n += 9; break; } n += 1; } n += o.IsOk ? 1000 : 0; return n; }
