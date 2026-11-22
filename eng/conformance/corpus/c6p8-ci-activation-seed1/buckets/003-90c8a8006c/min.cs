WriteLine(0);
public static class C
{
    public static Result<int, int> UsMk2(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
    public static int Us3(int p) { int n = 0; Result<int, int> o = UsMk2(p); switch (o) { case not Err<int> when p > 5: n += 100; break; case Ok<int>(var v) when p % 2 == 0: n += 50 + v; break; case var w: n += w.IsOk ? 300 : 400; break; } n += o.IsOk ? 1000 : 0; return n; }
}
