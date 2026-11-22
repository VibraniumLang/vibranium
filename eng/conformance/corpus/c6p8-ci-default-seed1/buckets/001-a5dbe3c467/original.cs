WriteLine(F(-2, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Us2(2) + Us2(5) * 10 + Us2(7) * 100;
    WriteLine(9);
    _ = p0 % 2;
    int.TryParse("81", out _);
    return acc;
}
static Option<Vec<int>> UsMk1(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us2(int p) { int n = 0; Option<Vec<int>> o = UsMk1(p); switch (o) { case not NoneType: n += 100; break; default: n += 9; break; } n += o.IsSome ? 1000 : 0; return n; }
