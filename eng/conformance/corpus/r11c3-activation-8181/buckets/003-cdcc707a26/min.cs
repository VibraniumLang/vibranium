WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    static int L1(int a) { return 0; }
    acc += P4(0, 0);
    #if VIBX
    WriteLine(1);
#endif
    acc += L1(0);
    acc += L2(0);
    static int L2(int a) => 0;
    return 0;
}
static int P4(int c0, int c1) => 0;
