WriteLine(C.M(1, true, -2L));
WriteLine(C.M(0, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        const int K1 = 1;
        for (int i2 = 0; i2 < 4; i2 += 1)
        {
            acc = K1;
            i2 /= 5;
        }
        {
            WriteLine(K1);
        }
        static int L3(int a)
        {
            WriteLine((4L - 6L) - ((long)a));
            return K1 + K1;
        }
        acc += L3(4);
        return acc;
    }
}
