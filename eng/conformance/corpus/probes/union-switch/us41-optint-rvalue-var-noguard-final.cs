WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkI(p)) { case Some<int>(var v) when v > 5: n += v; break; case var w: n += w.IsSome ? 100 : 200; break; } return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
