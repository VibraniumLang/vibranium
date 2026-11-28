WriteLine(F(-1, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    if (p0 is var q2 && q2 != 1)
    {
        acc += q2;
        WriteLine((p1 ? q2 : q2) < acc);
    }
    WriteLine(q2);
    int s3 = (7.25 % 7.25) - (p1 ? 9.25 : 2.25) switch { > 1.25 and < 6.25 when p0 != 1 => -1 - acc, _ => acc / (p1 ? acc : p0) };
    acc += s3;
    acc += L1(p0 % 1);
    static int L1(int a) => -2 << a;
    return acc;
}
