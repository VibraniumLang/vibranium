WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = 0;
        Result<int, int> x1 = Ok(0);
        switch (x1)
        {
            case Ok<int>(var v2) when false:
            case Err<int>(var e3): int _ = e3; break;
        }
        return acc;
    }
}
