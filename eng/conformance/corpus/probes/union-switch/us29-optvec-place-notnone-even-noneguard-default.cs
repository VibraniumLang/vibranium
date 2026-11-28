WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<Vec<int>> o = MkV(p); switch (o) { case not NoneType when p % 2 == 0: n += 100; break; case NoneType when p < 2: n += 3; break; case Some<Vec<int>>(var v): n += v.Count + v[0]; break; default: n += 7; break; } n += o.IsSome ? 1000 : 0; return n; }
static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
