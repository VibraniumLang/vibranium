WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    int s4;
    acc += Ca2((int)p2);
    s4 = acc;
    return 0;
}
static int Ca2(int p) { Vec<int> x = [p, 2]; var c = new Ck1(p); int r = c.Eat(x.AsReadOnlySpan(), Move(x)); return r; }
public sealed class Ck1(int z) { public int Z = z; public int Eat(ReadOnlySpan<int> a, Vec<int> b) { int k = Sink(b); return (a.Length > 0 ? a[0] : -1) + k; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
