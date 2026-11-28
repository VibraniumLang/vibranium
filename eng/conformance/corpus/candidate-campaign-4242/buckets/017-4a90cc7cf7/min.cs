WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += M3(ref acc, 3) + 0.0 < 0.0 ? 0 : 0;
    return acc;
}
static int M3(ref int p, int k)
{
    p += k;
    return 0;
}
