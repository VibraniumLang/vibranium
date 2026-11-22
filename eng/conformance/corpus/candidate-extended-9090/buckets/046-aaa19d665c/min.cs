WriteLine(C.M(0, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        acc += M8(ref acc, 4) + (false ? 0 : 0);
        return acc;
    }
    public static int M8(ref int p, int k)
    {
        p += k;
        return 0;
    }
}
