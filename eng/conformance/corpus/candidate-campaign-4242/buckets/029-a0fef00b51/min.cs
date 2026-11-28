WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    static int L1(int a) { return 0; }
    acc += L1(0);
    acc += L3(0);
    static int L3(int a) => 0;
    var s5 = 0.0 - 0.0 switch { _ => 0 };
    acc += s5;
    return 0;
}
