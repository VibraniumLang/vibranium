WriteLine(F(-1, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var m1 = new IntMap<int>(3);
    m1.Set(0, (int)((float)p2));
    for (int m1_i = 0; m1_i < 34; m1_i++) m1.Set(m1_i * 8, m1_i);
    if (acc is <= 6)
    {
        var x2 = p0;
    }
    else
    {
        var b3 = -1 != acc;
    }
    if (m1.Get(8) is Some<int>(var x4)) WriteLine(x4); else WriteLine(-1);
    WriteLine(m1.Count);
    WriteLine(m1.Contains(-2));
    acc += M5(ref acc, 1) + (!p1 ? p0 : p0);
    WriteLine(acc);
    double d6 = (double)acc;
    return acc;
}
static int M5(ref int p, int k)
{
    p += k;
    return p * 1;
}
