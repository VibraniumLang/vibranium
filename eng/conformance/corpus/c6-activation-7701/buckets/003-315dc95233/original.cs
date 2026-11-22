WriteLine(F(1, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    S s1 = new S();
    acc += Vw3(acc switch { -2 => -3, 4 => acc, 1 when p1 || p1 => 3, _ => p0 });
    bool b4;
    s1.A = acc;
    acc += Sum5(s1);
    WriteLine(9u);
    return acc;
}
static ReadOnlySpan<int> Sx2(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw3(int p) { Vec<int> a = [p, 5, 6]; var s = Sx2(a.AsReadOnlySpan()); return s.Length + a.Count; }
static int Sum5(in S x) => x.A + x.B;
public struct S
{
    public int A;
    public int B;
}
