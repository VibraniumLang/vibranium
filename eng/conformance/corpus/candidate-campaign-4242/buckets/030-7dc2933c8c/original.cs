WriteLine(C.M(4, true));
WriteLine(C.M(1, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        const long K1 = 7L;
        double d2 = ((double)K1) % (-1.25 + -3.25);
        acc += P3(p0 is > 2 and < 3 ? true : d2 > 1.0, p0);
        static int L4(int a) => P3(true, a) + (int)K1;
        acc += L4(p0);
        return acc;
    }
    public static int P3(bool c0, int c1) => c0 ? 1 : 0 + c1;
}
