WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkV(p)) { case Some<Vec<int>>(var v) when v.Count + p > 9: n += 100 + v.Count + v[0]; break; case Some<Vec<int>>(var v): n += v.Count + v[0]; break; case NoneType: n += 7; break; } return n; }
static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
