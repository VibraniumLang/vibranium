WriteLine(C.M(2, true, -2L));
WriteLine(C.M(5, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        S s1;
        s1.B = p0 % 3;
        var _ = (int)'b';
        acc += 8;
        {
            int.TryParse("10", out int _);
        }
        WriteLine(_);
        int.TryParse("20", out _);
        WriteLine(_);
        int.TryParse("75", out _);
        WriteLine(_);
        acc += s1.B;
        return acc;
    }
}
public struct S
{
    public int A;
    public int B;
}
