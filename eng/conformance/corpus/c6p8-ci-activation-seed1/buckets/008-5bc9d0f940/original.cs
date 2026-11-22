WriteLine(C.M(2, true));
WriteLine(C.M(8, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vm1(5);
        acc += Cr3(p0);
        var x6 = acc;
        acc += L4(acc);
        static int L4(int a)
        {
            double d5 = (double)a;
            return 6;
        }
        return acc;
    }
    public static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { Span<int> s = b.AsSpan(); t = s; n += t[0]; } n += t.Length; return n; }
    public static int Cr3(int p) { var x = new Nd2(p); var y = new Nd2(p + 1); int r = x.Peek(y); return r + x.V; }
}
public sealed class Nd2(int v) { public int V = v; public int Peek(Nd2 o) { int k = Sink(o); return V + k; } [Mut] public void Absorb(Nd2 o) { int k = Sink(o); V = V + k; } static int Sink(Nd2 x) => x.V; }
