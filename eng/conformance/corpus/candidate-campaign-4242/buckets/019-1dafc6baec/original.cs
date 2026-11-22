WriteLine(C.M(-2, true));
WriteLine(C.M(5, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (p0 is var q1)
        {
            acc += q1;
            if (-3 == p0)
            {
                long x2 = (long)q1;
            }
        }
        WriteLine(q1);
        acc += M3(ref acc, 2) + (p0 is > 0 and < 5 ? 7 - acc : p0);
        WriteLine(acc);
        return acc;
    }
    public static int M3(ref int p, int k)
    {
        p += k;
        return p * 2;
    }
}
