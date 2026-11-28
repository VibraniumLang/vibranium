WriteLine(F(5, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Vw2(p0);
    if (p0 is var q3 && q3 < 5)
    {
        acc += q3;
        WriteLine((3u + 0u) ^ (7u % 4u));
        acc += Ca8(1);
    }
    WriteLine(q3);
    return acc;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
static int Sink6(Hd5 x) => x.N.V;
static int Eat7(Hd5 b, in Nd4 a) { int k = Sink6(b); return a.V + k; }
static int Ca8(int p) { var x = new Hd5(new Nd4(p), [p, 2]); int r = Eat7(b: Move(x), a: in x.N); return r; }
public sealed class Nd4(int v) { public int V = v; public int Peek(Hd5 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd5 o) { int k = Sink(o); V = V + k; } static int Sink(Hd5 x) => x.N.V; }
public sealed class Hd5(Nd4 n, Vec<int> items) { public Nd4 N = n; public Vec<int> Items = items; }
