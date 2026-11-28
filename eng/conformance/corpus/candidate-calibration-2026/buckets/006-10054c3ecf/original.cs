WriteLine(F(3, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var _ = 4;
    acc += -1;
    S s2 = new S();
    acc += M1(ref s2, 5);
    int.TryParse("59", out _);
    WriteLine(_);
    _ = acc + 3;
    WriteLine(_);
    return acc;
}
static int M1(in S p, int k) { return p.A * 1; }
public struct S
{
    public int A;
    public int B;
}
