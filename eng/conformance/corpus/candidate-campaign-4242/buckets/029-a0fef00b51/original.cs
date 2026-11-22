WriteLine(F(-1, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    static int L1(int a)
    {
        bool b2;
        return (a != -1 ? -1 : 4) + (a * a);
    }
    if ((ushort)3 > ((ushort)acc))
    {
        ushort x4 = (ushort)(7 * acc);
    }
    acc += L1(acc);
    acc += L3(p0);
    static int L3(int a) => a;
    var s5 = (5.25 + 4.25) - (-1.25 + 9.25) switch { 1.25 when false => acc, _ => p0 + acc };
    acc += s5;
    acc += 2;
    return acc;
}
