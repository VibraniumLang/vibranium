WriteLine(C.M(0, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        var q4 = new Ctr3();
        acc += q4.Get();
        acc += M5(ref acc, 2) + (false ? 0 : 0);
        return acc;
    }
    public static int M5(ref int p, int k)
    {
        p += k;
        return 0;
    }
}
public struct Ctr3
{
    public int N;
    public readonly int Get() => N;
}
