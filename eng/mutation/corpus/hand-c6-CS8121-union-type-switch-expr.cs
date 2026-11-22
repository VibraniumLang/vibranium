int k = 4;
WriteLine(Mk(k) switch { Some<int>(var v) => v, Option<int> o => o.IsSome ? 1 : 0 });
static Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }
