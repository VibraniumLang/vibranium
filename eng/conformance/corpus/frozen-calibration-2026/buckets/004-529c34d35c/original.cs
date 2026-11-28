WriteLine(F(6, true, -2L));
WriteLine(F(6, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    uint x1 = (uint)(p0);
    switch (x1)
    {
        case < 0u: int x2 = p0 - acc; return acc;
        case >= 0u when !p1:
            if (acc is > 2 and < 6)
            {
                int x3 = p0;
            }
            acc += P4(!p1?true:p0 is 8, -3);
            int x5 = acc;
            return acc;
        default: return acc;
    }
}
static int P4(bool c0, int c1) => c0 ? 1 : 0 + c1;
