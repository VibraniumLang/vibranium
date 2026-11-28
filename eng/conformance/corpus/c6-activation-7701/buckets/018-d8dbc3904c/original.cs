WriteLine(C.M(0, true));
WriteLine(C.M(4, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vw2(p0);
        acc += Ca7(acc);
        long x8 = (long)acc;
        return acc;
    }
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
    public static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
    public static int Sink5(Hd4 x) => x.N.V;
    public static int Eat6(Hd4 b, in Vec<int> a) { int k = Sink5(b); return (a.Count > 0 ? a[0] : -1) + k; }
    public static int Ca7(int p) { var x = new Hd4(new Nd3(p), [p, 2]); int r = Eat6(x, in x.Items); return r; }
}
public sealed class Nd3(int v) { public int V = v; public int Peek(Hd4 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Hd4 o) { int k = Sink(o); V = V + k; } static int Sink(Hd4 x) => x.N.V; }
public sealed class Hd4(Nd3 n, Vec<int> items) { public Nd3 N = n; public Vec<int> Items = items; }
