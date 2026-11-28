WriteLine(C.N(0));
public static class C
{
    public static int M(ref readonly int p, int k) { return p + k; }
    public static int N(int k)
    {
        int x = 1;
        return M(ref x, k);
    }
}
