WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Us2(2) + Us2(3) * 10 + Us2(7) * 100;
    Result<int, int> u5 = Try3(0);
    return 0;
}
static Option<Vec<int>> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us2(int p) { int n = 0; Option<Vec<int>> o = UsMk1(p); switch (o) { case not NoneType: n += 100; break; default: n += 9; break; } n += o.IsSome ? 1000 : 0; return n; }
static Result<int, int> Try3(int k) => (Result<int, int>)Ok(0);
