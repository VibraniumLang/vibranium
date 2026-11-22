WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkI(p)) { case Some<int> s when s.Value > 5: n += s.Value * 10; break; case Some<int>(var v): n += v; break; case NoneType: n += 7; break; } return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
