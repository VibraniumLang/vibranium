// vib-conformance: file-name Program.g.cs
WriteLine(C.M(6, true, -2L));
WriteLine(C.M(9, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        if (acc is var q1)
        {
            acc += q1;
            acc += acc;
            acc += Us3(1) + Us3(3) * 10 + Us3(6) * 100;
        }
        return acc;
    }
    public static Result<int, int> UsMk2(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
    public static int Us3(int p) { int n = 0; Result<int, int> o = UsMk2(p); switch (o) { case not Err<int> when p > 5: n += 100; break; case Ok<int>(var v) when p % 2 == 0: n += 50 + v; break; case var w: n += w.IsOk ? 300 : 400; break; } n += o.IsOk ? 1000 : 0; return n; }
}
