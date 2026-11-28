WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Ct4(p1 ? p1 ? -1 : 3 : acc);
    acc += Us6(1) + Us6(4) * 10 + Us6(6) * 100;
    return 0;
}
static Vec<int> MakeV2() => [1, 2, 3];
static int Len3(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct4(int p) { bool c = p > 0; return Len3(c ? MakeV2().AsReadOnlySpan() : MakeV2().AsReadOnlySpan()[..1]); }
static Option<Vec<int>> UsMk5(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
static int Us6(int p) { int n = 0; Option<Vec<int>> o = UsMk5(p); switch (o) { case not NoneType: n += 100; break; default: n += 9; break; } n += o.IsSome ? 1000 : 0; return n; }
