WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; n += MkI(p) switch { var w when p > 5 => w.IsSome ? 100 : 200, Some<int>(var v) => v, _ => 7 }; return n; }
static Option<int> MkI(int a) { if (a > 2) return Some(a); return None; }
