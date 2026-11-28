WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    bool ok4 = false;
    WriteLine(ok4);
    bool ok5 = false;
    WriteLine(ok5);
    acc += Us2(2) + Us2(3) * 10 + Us2(8) * 100;
    return 0;
}
static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
static int Us2(int p) { int n = 0; switch (UsMk1(p)) { case not NoneType when p == 3: n += 100; break; case Some<int>(var v) when v > 5: n += 50 + v; break; case var w: n += w.IsSome ? 300 : 400; break; } return n; }
