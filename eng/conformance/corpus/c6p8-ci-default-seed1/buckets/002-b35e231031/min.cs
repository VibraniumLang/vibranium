WriteLine(0);
public static class C
{
    public static Result<Vec<int>, int> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
    public static int Us2(int p) { int n = 0; n += UsMk1(p) switch { Err<int> or Ok<Vec<int>> when p != 4 => 100, Ok<Vec<int>>(var v) => v.Count + v[0], Err<int>(var e) => e }; return n; }
}
