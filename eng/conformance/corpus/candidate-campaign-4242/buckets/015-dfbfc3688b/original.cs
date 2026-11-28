WriteLine(F(-1, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    S s1;
    s1.B = p0;
    const int K2 = 7;
    int x3 = p0 + 3;
    static int L4(int a) => (a switch { < 4 => a, >= 4 when 2.25 != 3.25 => 9 }) + K2;
    acc += L4(K2);
    acc += s1.B;
    return acc;
}
public struct S
{
    public int A;
    public int B;
}
