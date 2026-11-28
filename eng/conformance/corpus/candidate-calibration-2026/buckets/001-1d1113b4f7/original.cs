WriteLine(C.M(-2, true, -2L));
WriteLine(C.M(5, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Vec<int> v2 = [5];
        foreach (var e1 in v2)
        {
            var _ = p0 switch { < 1 => p0, >= 1 when true => -2 };
            WriteLine(_ is not 1);
            int s3 = p1 switch { true => 4, false => -2 * p0 };
            acc += s3;
            _ = p0;
            WriteLine(_);
            if (p1) continue;
            acc += e1;
        }
        acc += (7 + p0) * acc;
        return acc;
    }
}
