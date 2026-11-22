WriteLine(C.M(0, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        ReadOnlySpan<int> v2 = [0];
        foreach (var e1 in v2) acc += Us4(2) + Us4(5) * 10 + Us4(8) * 100;
        return acc;
    }
    public static Result<Vec<int>, int> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
    public static int Us4(int p) { int n = 0; Result<Vec<int>, int> o = UsMk3(p); switch (o) { case Ok<Vec<int>> or Err<int> when p < 3: n += 100; break; case not Err<int> when p == 5: n += 30; break; case Ok<Vec<int>>(var v): n += v.Count + v[0]; break; default: n += 9; break; } n += o.IsOk ? 1000 : 0; return n; }
}
