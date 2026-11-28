WriteLine(C.M(3, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        char x1 = 'b';
        int r2;
        switch (x1)
        {
            case < 'a': int.TryParse("3", out var o3); acc += o3; r2 = acc; break;
            case >= 'a':
                int.TryParse("3", out var o3);
                o3 = -1;
                acc += o3;
                acc = acc * 7;
                acc += P4(p0 is > 7 ? p0 : p1 ? p0 : acc, p0);
                int x5 = p0 << ((int)6L);
                r2 = acc;
                break;
        }
        acc += r2;
        return acc;
    }
    public static int P4(int c0, int c1) => c0 + c1;
}
