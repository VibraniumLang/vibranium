WriteLine(C.M(0, true));
WriteLine(C.M(5, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (!p1)
        {
            double d1 = !p1 ? -3.25 / 4.25 : 3.25;
        }
        acc += G2(p0 ^ p0)[0];
        var s3 = acc switch { < 0 when true => -1, >= 0 => (int)1.25 };
        acc += s3;
        if (p0 is not 1)
        {
            acc += p0 - ((int)(ushort)3);
        }
        else
        {
            int x4 = acc + 2;
        }
        return acc;
    }
    public static ReadOnlySpan<int> G2(int k) { return [7, 5]; }
}
