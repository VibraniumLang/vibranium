WriteLine(C.M(-1, true));
WriteLine(C.M(1, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += p0 + 7;
        acc += P3(p1 ? !p1 : p1, 7);
        acc += Cr2(acc);
        return acc;
    }
    public static int Cr2(int p) { var x = new Bg1(p); x += x; int r = 1; return r; }
    public static int P3(bool c0, int c1) => c0 ? 1 : 0 + c1;
}
public sealed class Bg1(int n) { public int N = n; [Mut] public void operator +=(Bg1 o) { N += Sink(o); } static int Sink(Bg1 x) => x.N; }
