WriteLine(0);
public static class C
{
    public static int Ca6(int p) { var x = new Hd4(new Nd3(p), [p, 2]); var c = new Ck5(p); int r = c.Eat(in x, Move(x.N)); return r; }
}
public sealed class Nd3(int v) { public int V = v; public int Peek(Hd4 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd4 o) { int k = Sink(o); V = V + k; } static int Sink(Hd4 x) => x.N.V; }
public sealed class Hd4(Nd3 n, Vec<int> items) { public Nd3 N = n; public Vec<int> Items = items; }
public sealed class Ck5(int z) { public int Z = z; public int Eat(ref readonly Hd4 a, Nd3 b) { int k = Sink(b); return a.N.V + k; } static int Sink(Nd3 x) => x.V; }
