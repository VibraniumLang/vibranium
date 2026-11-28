WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    int c1 = 0;
    while (c1 < 4)
    {
        c1 += 1;
        while (c1 is var q2)
        {
            acc += q2;
            ushort x3 = (ushort)(0 % acc);
            break;
        }
        c1 /= 5;
    }
    return 0;
}
