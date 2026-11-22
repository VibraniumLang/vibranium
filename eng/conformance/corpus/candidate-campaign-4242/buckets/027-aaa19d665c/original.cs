WriteLine(C.M(1, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        SO s1 = new SO(1, 1, [4]);
        WriteLine(1u * ((uint)acc));
        acc += M2(ref acc, 3) + (p1 && p1 ? acc : p0 + 5);
        WriteLine(acc);
        s1.B = (acc + p0) - acc;
        acc += Sum3(s1);
        int x4 = acc;
        return acc;
    }
    public static int M2(ref int p, int k)
    {
        p += k;
        return p * 3;
    }
    public static int Sum3(SO x) => x.A + x.B;
}
public struct SO(int a, int b, Vec<int> n)
{
    public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
