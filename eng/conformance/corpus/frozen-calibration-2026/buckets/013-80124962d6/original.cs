WriteLine(C.M(1, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        SO s1;
        s1.A = 1;
        s1.B = p0;
        s1.V = [2];
        SO t2 = s1;
        if (Try3(0).IsErr(out var v4, out var er5)) WriteLine(-1);
        acc += v4;
        int x6 = (p0 + acc) / 4;
        t2.A = p0 is > -2 and < 3 ? 8 : (int)2.5f;
        acc += t2.A;
        WriteLine(p1);
        return acc;
    }
    public static Result<int, int> Try3(int k) => k != 0 ? (Result<int, int>)Ok(k) : Err(0 - k);
}
public struct SO(int a, int b, Vec<int> n)
{
    public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
