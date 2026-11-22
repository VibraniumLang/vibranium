WriteLine(F(-2, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Vw5(2.25 != -2.25 ? p0 : (int)'d');
    long x6 = (long)acc;
    acc += Ct3(acc);
    return acc;
}
static Vec<int> MakeV1() => [1, 2, 3];
static int Len2(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct3(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV1().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len2(v.AsReadOnlySpan()); }
static int Vw5(int p) { var g = new Gv4([p, 1]); Vec<int> b = [3]; var s = g.Get(b.AsReadOnlySpan()); b.Add(9); return s[0]; }
public sealed class Gv4(Vec<int> v) { Vec<int> items = v; public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => items.AsReadOnlySpan(); }
