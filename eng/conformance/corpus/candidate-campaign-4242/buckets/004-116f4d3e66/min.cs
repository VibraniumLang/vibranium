WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        acc += M3(ref acc, 0);
        return 0;
    }
    public static int M3(in int p, int k) { return 0; }
}
