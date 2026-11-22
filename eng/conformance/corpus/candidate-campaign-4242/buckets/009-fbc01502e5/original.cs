WriteLine(F(-2, true, -2L));
WriteLine(F(6, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    const ushort K1 = (ushort)7;
    if ((p1 && p1) && (p0 < -1))
    {
        bool b2 = acc is > 0 and < 7;
    }
    int s3 = acc switch { 1 => acc + acc, 4 => acc < 5 ? acc : acc, > 0 and < 6 => p0, _ => p2 switch { < 5L when true => 9, >= 5L => acc } };
    acc += s3;
    WriteLine(K1);
    acc += M4(out acc, 1) + (p1 ? s3 : acc);
    WriteLine(acc);
    return acc;
}
static int M4(out int p, int k)
{
    p = k;
    return p * 1;
}
