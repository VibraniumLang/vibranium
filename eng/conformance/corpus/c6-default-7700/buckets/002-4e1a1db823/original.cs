WriteLine(F(4, true, -2L));
WriteLine(F(1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    int c1 = 0;
    while (c1 < 4)
    {
        c1 += 1;
        while (c1 is var q2 && q2 != 0)
        {
            acc += q2;
            ushort x3 = (ushort)(-2 % acc);
            break;
        }
        c1 /= 5;
    }
    return acc;
}
