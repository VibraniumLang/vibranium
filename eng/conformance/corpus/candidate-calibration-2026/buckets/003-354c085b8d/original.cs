WriteLine(F(1, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    {
        int x2 = (9 ^ acc) + p0;
        static int L1(int a) => a <= 0 ? 8 : a + L1(a - 1);
        acc += L1(acc);
    }
    acc += M3(out acc, 4) + (6L < -2L ? p0 : 1);
    WriteLine(acc);
    return acc;
}
static int M3(out int p, int k)
{
    p = k;
    return p * 1;
}
