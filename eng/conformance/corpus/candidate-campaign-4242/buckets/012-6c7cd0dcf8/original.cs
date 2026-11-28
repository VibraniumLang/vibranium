WriteLine(C.M(3, true, -2L));
WriteLine(C.M(-2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        if (acc is > 2 and var q1)
        {
            acc += q1;
            acc += q1;
        }
        acc += M2(out acc, 3) + (p1 ? p0 : acc);
        WriteLine(acc);
        return acc;
    }
    public static int M2(out int p, int k)
    {
        p = k;
        return p * 2;
    }
}
