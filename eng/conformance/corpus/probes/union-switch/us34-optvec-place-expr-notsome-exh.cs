WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<Vec<int>> o = MkV(p); n += o switch { not Some<Vec<int>> => 3, Some<Vec<int>>(var v) => v.Count + v[0] }; n += o.IsSome ? 1000 : 0; return n; }
static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
