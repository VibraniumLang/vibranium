WriteLine(C.M(2, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (p0 is int q1)
        {
            acc += q1;
            if (p1)
            {
                long x2 = (long)acc;
            }
        }
        var s3 = p0 switch { 1 => p0, > 2 => (int)((char)p0), int _ when true => (3 + acc) - p0 };
        acc += s3;
        return acc;
    }
}
