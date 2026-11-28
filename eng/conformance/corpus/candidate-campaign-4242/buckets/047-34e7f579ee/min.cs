WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += M3(ref acc, 4) + (false ? 0 : 0);
    return acc;
}
static int M3(ref int p, int k)
{
    p += k;
    return 0;
}
