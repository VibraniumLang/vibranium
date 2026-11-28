WriteLine(F(5, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var x6 = acc;
    acc += Vw5((p0 - 8) - 5);
    acc += As3((int)((ushort)(p0 - acc)));
    return acc;
}
static int As3(int p) { var x = new Hd2(new Nd1(p), [p, 2]); x.N.V = x.N.V + x.Items.Count; return x.N.V; }
static ReadOnlySpan<int> Sx4(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
static int Vw5(int p) { Vec<int> a = [p, 5, 6]; var s = Sx4(a.AsReadOnlySpan()); return s[0] + s.Length; }
public sealed class Nd1(int v) { public int V = v; public int Peek(Hd2 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd2 o) { int k = Sink(o); V = V + k; } static int Sink(Hd2 x) => x.N.V; }
public sealed class Hd2(Nd1 n, Vec<int> items) { public Nd1 N = n; public Vec<int> Items = items; }
