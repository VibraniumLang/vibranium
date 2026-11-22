WriteLine(C.M(-2, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        int c1 = 0;
        do
        {
            c1 += 1;
            int _ = c1;
            c1 = _ | (p0 & acc);
            int.TryParse("75", out _);
            WriteLine(_);
            _ = 6;
            WriteLine(_);
            WriteLine(_);
        }
        while (c1 < 2);
        return acc;
    }
}
