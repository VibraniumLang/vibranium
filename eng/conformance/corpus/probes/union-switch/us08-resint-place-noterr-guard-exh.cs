WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Result<int, int> o = MkRI(p); switch (o) { case not Err<int> when p > 5: n += 100; break; case Ok<int>(var v): n += v; break; case Err<int>(var e): n += e; break; } n += o.IsOk ? 1000 : 0; return n; }
static Result<int, int> MkRI(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
