WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; n += MkL(p) switch { Some<R>(var v) when p > 5 => 100 + Eat(v), Some<R>(var v) => Eat(v), NoneType => 7 }; return n; }
static Option<R> MkL(int a) { if (a > 2) return Some(new R(a)); return None; }
static int Eat(R r) { using var u = r; return u.Id; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
