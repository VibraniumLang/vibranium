WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    {
        var s2 = 0f % 0f switch { _ => 0 };
        acc += s2;
        static int L1(int a) => 0;
        acc += L1(0);
    }
    acc += First3([]);
    return 0;
}
static int First3(ReadOnlySpan<byte> q) => 0;
