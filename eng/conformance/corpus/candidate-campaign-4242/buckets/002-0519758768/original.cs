WriteLine(F(4, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p1)
    {
        double d1 = (double)p0;
    }
    var s2 = acc switch { < 4 => 1, >= 4 when true => acc };
    acc += s2;
    acc += M3(ref s2, 1);
    WriteLine(s2);
    return acc;
}
static int M3(ref readonly int p, int k) { return p * 1; }
