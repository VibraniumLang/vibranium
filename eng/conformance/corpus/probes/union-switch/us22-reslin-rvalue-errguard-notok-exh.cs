WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkRL(p)) { case Err<int>(var e) when e > 21: n += e; break; case not Ok<R>: n += 3; break; case Ok<R>(var v): n += Eat(v); break; } return n; }
static Result<R, int> MkRL(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
static int Eat(R r) { using var u = r; return u.Id; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
