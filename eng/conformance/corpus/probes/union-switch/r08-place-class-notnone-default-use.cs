WriteLine(Us(1) + Us(4) * 10 + Us(8) * 100);
static int Us(int p) { int n = 0; Option<Nd> o = MkC(p); switch (o) { case not NoneType: n += 100; break; default: n += 9; break; } n += o.IsSome ? 1000 : 0; return n; }
static Option<Nd> MkC(int a) { if (a > 2) return Some(new Nd(a)); return None; }
public sealed class Nd(int v) { public int V = v; }
