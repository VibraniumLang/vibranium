WriteLine(F(3, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    if (!(p1 && p1))
    {
        ushort x1 = (ushort)(acc % 3);
    }
    else
    {
        int x2 = acc + 9;
    }
    acc += P3(p0 != 3 ? p0 : p0, p0);
    acc += M4(in p0 + 2, 5) + (!p1 ? acc - 4 : P3(acc, p0));
    return acc;
}
static int P3(int c0, int c1) => c0 + c1;
static int M4(ref readonly int p, int k) { return p * 3; }
