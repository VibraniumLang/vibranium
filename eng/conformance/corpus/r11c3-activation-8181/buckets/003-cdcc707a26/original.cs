#define VIBX
WriteLine(F(-1, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    static int L1(int a)
    {
        WriteLine((a < 2) && ((a < 5) && (a < 1)));
        return a;
    }
    int x3 = !p1 ? p0 : p0;
    acc += P4((p0 > 3) && p1 ? p0 : acc, p0);
    #if VIBX
    WriteLine(1);
#endif
    acc += L1((int)(byte)2);
    acc += L2(p0);
    static int L2(int a) => a;
    return acc;
}
static int P4(int c0, int c1) => c0 + c1;
