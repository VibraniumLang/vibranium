WriteLine(F(-1, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [p0, acc, p0];
    foreach (var e1 in v2)
    {
        switch (p1)
        {
            case true: int x3 = v2.Length; break;
            case false:
                x3 = e1;
                acc += x3;
                const long K4 = 6L;
                WriteLine(p1);
                {
                    WriteLine(K4);
                }
                int x5 = e1;
                break;
        }
        if (p1) continue;
        ushort x6 = (ushort)(acc);
        acc += e1;
    }
    long x7 = (long)acc;
    return acc;
}
