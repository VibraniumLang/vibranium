WriteLine(C.M(-1, true, -2L));
WriteLine(C.M(-3, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        if (acc is 3)
        {
            ushort x1 = (ushort)(9 * p0);
        }
        else
        {
            long x2 = p2;
        }
        nint w3 = (nint)(acc + 4);
        WriteLine(w3 / (nint)3);
        acc += M4(ref acc, 1) + ((ushort)3 >= (ushort)1 ? acc : acc);
        WriteLine(acc);
        double w5 = (double)(acc + 4);
        WriteLine((int)(w5 - (p1 ? w5 : w5)));
        return acc;
    }
    public static int M4(in int p, int k) { return p * 2; }
}
