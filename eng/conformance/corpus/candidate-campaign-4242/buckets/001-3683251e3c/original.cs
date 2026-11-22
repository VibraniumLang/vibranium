WriteLine(F(3, true, -2L));
WriteLine(F(1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    int _ = 2;
    acc -= _;
    _ *= 3;
    WriteLine(_);
    WriteLine(_);
    acc += M1(ref _, 1);
    WriteLine(_);
    WriteLine((int)(p2 << _));
    if (((ushort)p2) > ((ushort)5))
    {
        VibraniumRuntime.Panic("bad state");
        return (p1 ? 1 : acc) - -1;
    }
    return acc;
}
static int M1(in int p, int k) { return p * 3; }
