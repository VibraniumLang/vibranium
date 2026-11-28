WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Us2(0) + Us2(5) * 10 + Us2(8) * 100;
    Init4(out var s3);
    acc += Sum8(s3);
    return acc;
}
static Result<Vec<int>, int> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
static int Us2(int p) { int n = 0; Result<Vec<int>, int> o = UsMk1(p); switch (o) { case not Err<int> when p % 2 == 0: n += 100; break; case not Err<int> when p > 5: n += 30; break; case Ok<Vec<int>>(var v) when p > 5: n += 50 + v.Count + v[0]; break; case Ok<Vec<int>>(var v): n += v.Count + v[0]; break; case Err<int>(var e): n += e; break; } n += o.IsOk ? 1000 : 0; return n; }
static void Init4(out S o)
{
    o.A = 0;
    o.B = 0;
}
static int Sum8(S x) => 0;
public struct S
{
    public int A;
    public int B;
}
