int k = 4;
Option<int> a = Mk(k);
if (a is Option<int> b) WriteLine(b.IsSome);
static Option<int> Mk(int a) { if (a > 2) return Some(a); return None; }
