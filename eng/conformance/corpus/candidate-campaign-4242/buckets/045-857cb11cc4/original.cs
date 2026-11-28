WriteLine(F(6, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    while (p0 is var q1)
    {
        acc += q1;
        int K2 = 1;
        int x3 = p0 + -3;
        acc += M4(out K2, 5);
        switch (x3)
        {
            case K2: WriteLine(1); break;
            default: WriteLine(2); break;
        }
        break;
    }
    return acc;
}
static int M4(out int p, int k)
{
    p = k;
    return p * 3;
}
