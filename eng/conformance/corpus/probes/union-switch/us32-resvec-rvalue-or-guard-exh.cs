WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkRV(p)) { case Ok<Vec<int>> or Err<int> when p > 5: n += 100; break; case Ok<Vec<int>>(var v): n += v.Count + v[0]; break; case Err<int>(var e): n += e; break; } return n; }
static Result<Vec<int>, int> MkRV(int a) { if (a > 2) { Vec<int> v = [a, a + 1]; return Ok(v); } return Err(a + 20); }
