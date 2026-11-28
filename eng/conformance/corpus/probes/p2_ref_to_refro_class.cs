WriteLine(C.M());
public static class C
{
    public static int M()
    {
        int x = 1;
        return N(ref x, 0);
    }
    public static int N(ref readonly int p, int k) { return p + k; }
}
