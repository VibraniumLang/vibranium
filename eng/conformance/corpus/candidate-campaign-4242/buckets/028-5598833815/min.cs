WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    static int L1(int a) => 0;
    var s4 = 0f % 0f switch { _ => 0 };
    acc += s4;
    acc += L1(0);
    acc += L2(0);
    static int L2(int a) => 0;
    return 0;
}
