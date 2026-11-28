WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Ca4(p0);
    var s5 = 0;
    acc += s5;
    return 0;
}
static int Ca4(int p) { var x = new Hd2(new Nd1(p), [p, 2]); var c = new Ck3(p); int r = c.Eat(in x.N, x); return r; }
public sealed class Nd1(int v) { public int V = v; public int Peek(Hd2 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd2 o) { int k = Sink(o); V = V + k; } static int Sink(Hd2 x) => x.N.V; }
public sealed class Hd2(Nd1 n, Vec<int> items) { public Nd1 N = n; public Vec<int> Items = items; }
public sealed class Ck3(int z) { public int Z = z; public int Eat(ref readonly Nd1 a, Hd2 b) { int k = Sink(b); return a.V + k; } static int Sink(Hd2 x) => x.N.V; }
