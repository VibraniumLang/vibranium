WriteLine(F(6, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    long x1 = (long)p0;
    switch (x1)
    {
        case < 6L: ushort x2 = (ushort)(p0); break;
        case >= 6L: break;
    }
    acc += M3(ref acc, 4);
    WriteLine(acc);
    acc += acc;
    return acc;
}
static int M3(in int p, int k) { return p * 1; }
