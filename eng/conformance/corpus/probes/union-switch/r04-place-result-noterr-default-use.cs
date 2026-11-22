WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Result<Vec<int>, int> o = MkR(p); switch (o) { case not Err<int>: n += 100; break; default: n += 9; break; } n += o.IsOk ? 1000 : 0; return n; }
static Result<Vec<int>, int> MkR(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
