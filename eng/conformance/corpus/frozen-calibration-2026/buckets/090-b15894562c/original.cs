WriteLine(F(0, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    SN n2;
    n2.In.A = acc;
    n2.In.B = p0;
    n2.C = 4 + p0;
    const int K3 = 2;
    if (p1)
    {
        bool b4 = p1;
    }
    int L5(int a) => 4 + K3;
    acc += L5(3);
    acc += n2.In.A + n2.C;
    return acc;
}
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
