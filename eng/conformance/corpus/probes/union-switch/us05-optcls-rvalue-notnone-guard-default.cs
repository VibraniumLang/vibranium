WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; switch (MkC(p)) { case not NoneType when p > 5: n += 100; break; case Some<Nd>(var v): n += v.V; break; default: n += 7; break; } return n; }
static Option<Nd> MkC(int a) { if (a > 2) return Some(new Nd(a)); return None; }
public sealed class Nd(int v) { public int V = v; }
