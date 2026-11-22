WriteLine(F(-2, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Us2(2) + Us2(4) * 10 + Us2(7) * 100;
    WriteLine(8u);
    acc += L3(p0);
    static int L3(int a)
    {
        var x4 = 0 / (8 >> a);
        return 5;
    }
    return acc;
}
static Result<Vec<int>, int> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
static int Us2(int p) { int n = 0; n += UsMk1(p) switch { not Ok<Vec<int>> => 3, Ok<Vec<int>>(var v) => v.Count + v[0] }; return n; }
