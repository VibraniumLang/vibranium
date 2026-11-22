WriteLine(C.M(0, false, 0L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = 0;
        acc += M2(out acc, 3) + (false ? 0 : 0);
        return acc;
    }
    public static int M2(out int p, int k)
    {
        p = k;
        return 0;
    }
}
