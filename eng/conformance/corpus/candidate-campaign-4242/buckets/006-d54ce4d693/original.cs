WriteLine(F(4, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    S s1;
    s1.B = acc;
    s1.A = p0;
    bool b2 = p1;
    acc += s1.B;
    acc += M3(ref acc, 1);
    WriteLine(acc);
    return acc;
}
static int M3(in int p, int k) { return p * 1; }
public struct S
{
    public int A;
    public int B;
}
