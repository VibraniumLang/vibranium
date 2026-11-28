WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Us2(0) + Us2(5) * 10 + Us2(8) * 100;
    var s3 = 0;
    acc += s3;
    return 0;
}
static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
static int Us2(int p) { int n = 0; Option<int> o = UsMk1(p); switch (o) { case not Some<int> when p < 3: n += 3; break; case Some<int>(var v) when v > 5: n += 50 + v; break; case var w: n += w.IsSome ? 300 : 400; break; } n += o.IsSome ? 1000 : 0; return n; }
