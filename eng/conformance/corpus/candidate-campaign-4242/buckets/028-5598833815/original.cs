WriteLine(F(0, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    static int L1(int a) => a <= 0 ? a * 0 : a + L1(a - 1);
    bool b3 = (5 + p0) >= -1;
    var s4 = 5.5f % 2.5f switch { < -1.5f => p0, _ => p0 };
    acc += s4;
    acc += L1(acc);
    acc += L2(8);
    static int L2(int a) => (int)(2.5f % 5.5f);
    return acc;
}
