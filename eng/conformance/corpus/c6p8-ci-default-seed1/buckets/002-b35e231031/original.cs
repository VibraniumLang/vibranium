WriteLine(C.M(1, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (acc is var q3 && q3 > 2)
        {
            acc += q3;
            var x4 = q3;
        }
        acc += Us2(1) + Us2(4) * 10 + Us2(6) * 100;
        return acc;
    }
    public static Result<Vec<int>, int> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
    public static int Us2(int p) { int n = 0; n += UsMk1(p) switch { Err<int> or Ok<Vec<int>> when p != 4 => 100, Ok<Vec<int>>(var v) => v.Count + v[0], Err<int>(var e) => e }; return n; }
}
