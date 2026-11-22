WriteLine(C.M(-1, true, -2L));
WriteLine(C.M(-2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        long K1 = 2L;
        var sm2 = new StrMap<int>(8);
        _ = sm2.Set("a\n", p0 + 9);
        _ = sm2.Set("a\n", acc);
        _ = sm2.Set("", p0);
        _ = sm2.Set("a"u8, 9);
        if ((6 % acc) >= p0)
        {
            double d3 = (double)(-3L + K1);
        }
        else
        {
            var b4 = p1;
        }
        WriteLine((int)(7.5f % 8.5f));
        if (acc is > -1 and < 6)
        {
            VibraniumRuntime.Panic("boom");
            return acc;
        }
        acc += sm2.Get("a").UnwrapOr(-1);
        WriteLine(sm2.Count);
        switch (p2)
        {
            case K1: WriteLine(1); break;
            default: WriteLine(2); break;
        }
        var d5 = (double)acc;
        return acc;
    }
}
