WriteLine(F(4, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Vec<int> v2 = [4, 3, acc];
    foreach (var e1 in v2)
    {
        ushort x5 = (ushort)(e1 + 5);
        acc += Us4(2) + Us4(5) * 10 + Us4(8) * 100;
        int x6 = e1 + -3;
        acc += e1;
    }
    return acc;
}
static Option<Vec<int>> UsMk3(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us4(int p) { int n = 0; Option<Vec<int>> o = UsMk3(p); for (int i = 0; i < 2; i++) { switch (o) { case not NoneType: n += 100; continue; case NoneType: n += 7; break; } n += 1; } n += o.IsSome ? 1000 : 0; return n; }
