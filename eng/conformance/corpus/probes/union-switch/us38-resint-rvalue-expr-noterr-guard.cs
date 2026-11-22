WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; n += MkRI(p) switch { not Err<int> when p > 5 => 100, Ok<int>(var v) => v, Err<int>(var e) => e }; return n; }
static Result<int, int> MkRI(int a) { if (a > 2) return Ok(a); return Err(a + 20); }
