WriteLine(F(0, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    bool b1 = p1;
    acc += M2(ref acc, 3) + (acc is <= 6 ? b1 ? -3 : acc : 9);
    WriteLine(acc);
    var s3 = acc switch { 6 => p0, _ => acc };
    acc += s3;
    acc += M4(ref s3, 1);
    WriteLine(s3);
    if (s3 is > 2 and < 6)
    {
        var x5 = s3 ^ s3;
    }
    return acc;
}
static int M2(ref int p, int k)
{
    p += k;
    return p * 3;
}
static int M4(in int p, int k) { return p * 1; }
