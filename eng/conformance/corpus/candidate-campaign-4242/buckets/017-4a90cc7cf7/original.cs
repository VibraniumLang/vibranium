WriteLine(F(6, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    if (p0 is int q1)
    {
        acc += q1;
        var b2 = p1;
        acc += M3(ref acc, 3) + (6.25 < 4.25 ? 6 : 9);
        WriteLine(acc);
    }
    WriteLine(q1);
    return acc;
}
static int M3(ref int p, int k)
{
    p += k;
    return p * 3;
}
