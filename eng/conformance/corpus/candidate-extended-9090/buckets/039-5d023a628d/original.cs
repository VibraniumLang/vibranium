WriteLine(F(-1, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    int c1 = 0;
    do
    {
        c1 += 1;
        var m2 = new IntMap<int>(5);
        m2.Set(1, !p1 ? 4 : p1 ? -2 : -2);
        if (acc is > 2 and < 3)
        {
            bool b3 = (p1 || p1) || (8L == 8L);
        }
        else
        {
            int x4 = acc / 1;
        }
        if (m2.Get(5) is Some<int>(var x5)) WriteLine(x5); else WriteLine(-1);
        WriteLine(m2.Count);
        WriteLine(m2.Contains(0));
    }
    while (c1 < 2);
    if (p0 is > 2 and < 4)
    {
        var x6 = 3 == acc ? (int)(byte)1 : (int)'a';
    }
    return acc;
}
