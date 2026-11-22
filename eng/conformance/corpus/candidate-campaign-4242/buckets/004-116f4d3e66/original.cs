WriteLine(C.M(3, true));
WriteLine(C.M(-3, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (p0 is var q1)
        {
            acc += q1;
            var b2 = q1 < 5;
            acc += M3(ref acc, 2);
            WriteLine(acc);
            _ = TryGet4(p0, out var _);
        }
        WriteLine(q1);
        return acc;
    }
    public static int M3(in int p, int k) { return p * 1; }
    public static bool TryGet4(int k, out int v)
    {
        v = k * 3;
        return k > 0;
    }
}
