WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkI(p)) { case not Some<int>: n += 3; break; case Some<int>(var v): n += v; break; } return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
