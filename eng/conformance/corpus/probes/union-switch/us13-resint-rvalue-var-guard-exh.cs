WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkRI(p)) { case var w when p > 5: n += w.IsOk ? 100 : 200; break; case Ok<int>(var v): n += v; break; case Err<int>(var e): n += e; break; } return n; }
static Result<int, int> MkRI(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
