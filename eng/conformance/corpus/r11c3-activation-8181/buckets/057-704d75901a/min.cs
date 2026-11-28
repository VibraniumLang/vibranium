WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [];
    foreach (ref var e1 in v2) acc += Ct5((acc + p0) << 0);
    return 0;
}
static Vec<int> MakeV3() => [1, 2, 3];
static int Len4(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct5(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV3().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len4(v.AsReadOnlySpan()); }
public sealed class Zc6 { public Vec<int> Items; public int K; public Zc6(int k) { Items = [k]; K = k; K += Peek(); } int Peek() => Items.Count; }
