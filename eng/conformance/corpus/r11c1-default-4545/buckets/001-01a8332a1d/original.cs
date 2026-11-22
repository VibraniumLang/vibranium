WriteLine(C.M(3, true, -2L));
WriteLine(C.M(1, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Result<int, int> x1 = Ok(acc >> acc);
        switch (x1)
        {
            case Ok<int>(var v2) when false:
            case Err<int>(var e3):
                int _ = -2 <= e3 ? p0 : e3 - -1;
                int x4 = p0;
                int.TryParse("44", out _);
                WriteLine(_);
                WriteLine(_);
                break;
        }
        var x5 = (5L + p2) / (7L + p2);
        return acc;
    }
}
