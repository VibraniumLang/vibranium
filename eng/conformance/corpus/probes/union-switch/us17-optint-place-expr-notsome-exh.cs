WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<int> o = MkI(p); n += o switch { not Some<int> => 3, Some<int>(var v) => v }; n += o.IsSome ? 1000 : 0; return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
