WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Us2(0) + Us2(5) * 10 + Us2(7) * 100;
    S s3;
    s3.B = 0;
    S t4;
    t4.A = 0;
    return 0;
}
static Result<int, int> UsMk1(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
static int Us2(int p) { int n = 0; switch (UsMk1(p)) { case Ok<int>(var v) when p >= 5: n += 50 + v; break; case var w: n += w.IsOk ? 300 : 400; break; } return n; }
public struct S
{
    public int A;
    public int B;
}
