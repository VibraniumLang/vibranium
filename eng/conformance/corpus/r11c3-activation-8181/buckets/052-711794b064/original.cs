WriteLine(C.M(2, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Ca6(p0);
        acc += 8L != 1L ? 7 : p0 / acc;
        acc += Ca2(5);
        return acc;
    }
    public static int Ca2(int p) { Vec<int> x = [p, 2]; int r = new Cw1(x[0..1], x).K; return r; }
    public static int Ca6(int p) { var x = new Hd4(new Nd3(p), [p, 2]); var c = new Ck5(p); int r = c.Eat(in x, Move(x.N)); return r; }
}
public sealed class Cw1 { public int K; public Cw1(Span<int> a, Vec<int> b) { int t = Sink(b); K = (a.Length > 0 ? a[0] : -1) + t; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
public sealed class Nd3(int v) { public int V = v; public int Peek(Hd4 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd4 o) { int k = Sink(o); V = V + k; } static int Sink(Hd4 x) => x.N.V; }
public sealed class Hd4(Nd3 n, Vec<int> items) { public Nd3 N = n; public Vec<int> Items = items; }
public sealed class Ck5(int z) { public int Z = z; public int Eat(ref readonly Hd4 a, Nd3 b) { int k = Sink(b); return a.N.V + k; } static int Sink(Nd3 x) => x.V; }
