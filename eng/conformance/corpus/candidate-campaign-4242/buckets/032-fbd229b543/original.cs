WriteLine(F(5, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    SN n2;
    n2.In.A = -3;
    n2.In.B = (p1 ? 0 : 6) % acc;
    n2.C = p0;
    acc += acc;
    acc += n2.In.A + n2.C;
    ref int r4 = ref acc;
    acc += M3(ref r4, 4);
    WriteLine(r4);
    return acc;
}
static int M3(in int p, int k) { return p * 1; }
public struct S
{
    public int A;
    public int B;
}
public struct SN
{
    public S In;
    public int C;
}
