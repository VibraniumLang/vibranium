WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Result<R, int> o = MkRL(p); n += o switch { Ok<R>(var v) => Eat(v), Err<int>(var e) when e > 21 => e, Err<int>(var e) => e + 1 }; return n; }
static Result<R, int> MkRL(int a) { if (a > 2) return Ok(new R(a)); return Err(a + 20); }
static int Eat(R r) { using var u = r; return u.Id; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
