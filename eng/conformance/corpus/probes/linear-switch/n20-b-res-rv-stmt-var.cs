WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkRL(p)) { case var u: n += TakeR(u); break; } return n; }
static Result<R, int> MkRL(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
static int TakeR(Result<R, int> u) { int n = 0; switch (Move(u)) { case Ok<R>(var v): n += Eat(v); break; case Err<int>(var e): n += e; break; } return n; }
static int Eat(R r) { using var u = r; return u.Id; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
