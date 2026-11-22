WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += M5(ref acc, 1) + (false ? 0 : 0);
    return acc;
}
static int M5(ref int p, int k)
{
    p += k;
    return 0;
}
