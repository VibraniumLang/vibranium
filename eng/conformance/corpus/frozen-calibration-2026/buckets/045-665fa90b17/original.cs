WriteLine(F(2, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    long x1 = (long)acc;
    int? nq = acc > 0 ? acc : null;
    WriteLine(nq ?? 0);
    bool ok3 = TryGet2(-2 - (acc >> p0), out _);
    WriteLine(ok3);
    if (p0 >= acc)
    {
        WriteLine(p0 > 4);
    }
    return acc;
}
static bool TryGet2(int k, out int v)
{
    v = k * 3;
    return k > 0;
}
