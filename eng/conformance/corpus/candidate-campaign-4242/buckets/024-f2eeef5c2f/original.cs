WriteLine(F(3, true, -2L));
WriteLine(F(9, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    {
        acc -= acc;
        var s2 = 0.5f % ((float)2) switch { >= 2.5f => 4, _ => p0 };
        acc += s2;
        static int L1(int a) => 0;
        acc += L1(4 - acc);
    }
    acc += First3([]);
    if (!p1)
    {
        int x4 = (acc + 5) + ((int)p2);
    }
    return acc;
}
static int First3(ReadOnlySpan<byte> q) => q.Length > 0 ? (int)q[0] : -1;
