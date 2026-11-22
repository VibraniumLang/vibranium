WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<R> o = MkL(p); switch (Move(o)) { case NoneType: n += 7; break; case Some<R>(var v): n += Eat(v); break; default: n += 9; break; } return n; }
static Option<R> MkL(int a) { if (a > 2) return Some(new R(a)); return None; }
static int Eat(R r) { using var u = r; return u.Id; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
