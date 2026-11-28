WriteLine(F(5, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (acc is int q1)
    {
        acc += q1;
        var x2 = acc;
    }
    WriteLine(q1);
    acc += M3(ref acc, 4) + (p1 && p1 ? acc : -3);
    WriteLine(acc);
    return acc;
}
static int M3(ref int p, int k)
{
    p += k;
    return p * 2;
}
