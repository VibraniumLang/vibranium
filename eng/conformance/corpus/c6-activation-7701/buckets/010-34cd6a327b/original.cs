WriteLine(F(5, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Ca2(p0);
    if (((double)acc) != (3.25 - 8.25))
    {
        int x5 = p0;
    }
    acc += Vw4(acc is not 5 ? (int)(byte)1 : 8);
    acc += Fail6(p0);
    return acc;
}
static int Ca2(int p) { Vec<int> x = [p, 2]; int r = new Cw1(b: x, a: x).K; return r; }
static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan()); return s[0] + s.Length; }
static int Fail6(int k)
{
    if (k > 3) VibraniumRuntime.Panic("boom");
    return k;
}
public sealed class Cw1 { public int K; public Cw1(Vec<int> b, in Vec<int> a) { int t = Sink(b); K = (a.Count > 0 ? a[0] : -1) + t; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
