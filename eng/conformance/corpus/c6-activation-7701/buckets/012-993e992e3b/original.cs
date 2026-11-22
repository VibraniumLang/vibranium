WriteLine(F(-1, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Cr4(acc);
    long x5 = (long)p0;
    acc += Vw2(p1 ? p0 : (int)5L);
    return acc;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s.Length + a.Count; }
static int Cr4(int p) { var x = new Rst3([p, 2]); var y = new Rst3([p + 1, 3]); int r = x.R(y); return r + x.R(new Rst3([4])); }
public readonly struct Rst3(Vec<int> v) { readonly Vec<int> V = v; public int R(Rst3 o) { int k = Sink(o); return V.Count > 0 ? V[0] + k : k; } static int Sink(Rst3 x) => 1; }
