WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; n += MkV(p) switch { Some<Vec<int>> or NoneType when p > 5 => 100, Some<Vec<int>>(var v) => v.Count + v[0], _ => 7 }; return n; }
static Option<Vec<int>> MkV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Some(v); } return None; }
