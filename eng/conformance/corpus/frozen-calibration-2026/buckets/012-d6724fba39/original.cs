WriteLine(F(4, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    ushort x1 = (ushort)(acc);
    switch (x1)
    {
        case < (ushort)1:
            acc *= p0 switch { < 3 => acc, >= 3 => acc };
            bool b3 = p0 is int q2 && q2 > 0;
            WriteLine(b3);
            break;
        case >= (ushort)1: break;
    }
    ref int r5 = ref acc;
    acc += M4(ref r5, 1);
    WriteLine(r5);
    return acc;
}
static int M4(ref readonly int p, int k) { return p * 1; }
