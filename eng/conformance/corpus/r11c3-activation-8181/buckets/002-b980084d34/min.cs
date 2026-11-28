WriteLine(0);
public static class C
{
    public static int Cr2(int p) { var x = new Bg1(p); x += x; int r = 1; return r; }
}
public sealed class Bg1(int n) { public int N = n; [Mut] public void operator +=(Bg1 o) { N += Sink(o); } static int Sink(Bg1 x) => x.N; }
