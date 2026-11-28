WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; n += MkC(p) switch { Some<Nd> or NoneType when p > 5 => 100, Some<Nd>(var v) => v.V, NoneType => 7 }; return n; }
static Option<Nd> MkC(int a) { if (a > 2) return Some(new Nd(a)); return None; }
public sealed class Nd(int v) { public int V = v; }
