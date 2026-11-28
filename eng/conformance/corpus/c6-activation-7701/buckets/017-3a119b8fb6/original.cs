WriteLine(F(3, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += new Zc1(1).K;
    bool b4 = p1;
    acc += Vw3(acc | (p0 - p0));
    return acc;
}
static ReadOnlySpan<int> Sx2(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw3(int p) { Vec<int> a = [p, 5, 6]; var s = Sx2(a.AsReadOnlySpan()); return s[0] + s.Length; }
public sealed class Zc1 { public Vec<int> Items; public int K; public Zc1(int k) { K = Peek(); Items = [k]; } int Peek() => Items.Count; }
