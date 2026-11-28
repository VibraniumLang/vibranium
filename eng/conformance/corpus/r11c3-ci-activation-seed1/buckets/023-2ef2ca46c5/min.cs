WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Ca3((p0 + acc) * 6);
    acc += Ca5(((int)0L) - p0);
    return 0;
}
static int Ca3(int p) { Option<Nd1> x = Some(new Nd1(p)); int r = new Cw2(in x, x.Unwrap()).K; return r; }
static int Ca5(int p) { Vec<int> x = [p, 2]; int r = new Cw4(x.AsReadOnlySpan()[0..1], x).K; return r; }
public sealed class Nd1(int v) { public int V = v; }
public sealed class Cw2 { public int K; public Cw2(in Option<Nd1> a, Nd1 b) { int t = Sink(b); K = (a.IsSome ? 1 : 0) + t; } static int Sink(Nd1 x) => x.V; }
public sealed class Cw4 { public int K; public Cw4(ReadOnlySpan<int> a, Vec<int> b) { int t = Sink(b); K = (a.Length > 0 ? a[0] : -1) + t; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
