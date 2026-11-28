WriteLine(F(-1, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Us2(0) + Us2(5) * 10 + Us2(8) * 100;
    acc += p0;
    var s3 = p0 switch { 1 when !p1 => p1 || p1 ? (int)'b' : p0, 2 or 8 => p0, _ => acc + 8 };
    acc += s3;
    return acc;
}
static Option<int> UsMk1(int a) { if (a > 2) return Some(a); return None; }
static int Us2(int p) { int n = 0; Option<int> o = UsMk1(p); switch (o) { case not Some<int> when p < 3: n += 3; break; case Some<int>(var v) when v > 5: n += 50 + v; break; case var w: n += w.IsSome ? 300 : 400; break; } n += o.IsSome ? 1000 : 0; return n; }
