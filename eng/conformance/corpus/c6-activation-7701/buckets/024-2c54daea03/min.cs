WriteLine(0);
public static class C
{
    public static int Ca2(int p) { Vec<int> x = [p, 2]; var c = new Ck1(p); int r = c.Eat(Move(x), in x); return r; }
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
}
public sealed class Ck1(int z) { public int Z = z; public int Eat(Vec<int> b, in Vec<int> a) { int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
