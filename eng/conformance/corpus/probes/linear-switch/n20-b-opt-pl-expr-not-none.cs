WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<R> o = MkL(p); n += o switch { not NoneType => 5, NoneType => 7 }; return n; }
static Option<R> MkL(int a) { if (a > 2) return Some(new R(a)); return None; }
public sealed class R(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
