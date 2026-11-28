WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    S s1;
    acc += Vw3(acc switch { -2 => -3, 4 => acc, 1 when p1 || p1 => 3, _ => p0 });
    acc += Sum5(s1);
    return 0;
}
static ReadOnlySpan<int> Sx2(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw3(int p) { Vec<int> a = [p, 5, 6]; var s = Sx2(a.AsReadOnlySpan()); return s.Length + a.Count; }
static int Sum5(in S x) => 0;
public struct S
{
}
