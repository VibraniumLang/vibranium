WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<int> o = MkI(p); switch (o) { case Some<int> s when p > 5: n += s.Value * 10; break; case Some<int>(var v): n += v; break; default: n += 7; break; } n += o.IsSome ? 1000 : 0; return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
