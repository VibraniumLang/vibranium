WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        int x4 = 0;
        ref readonly int r6 = ref x4;
        acc += M5(ref r6, 0);
        return 0;
    }
    public static int M5(ref readonly int p, int k) { return 0; }
}
