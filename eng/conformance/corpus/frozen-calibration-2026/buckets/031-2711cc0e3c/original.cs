WriteLine(C.M(-2, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        switch (p1)
        {
            case false: break;
            default:
                static int L1(int a) => 0;
                acc += acc ^ (acc + -2);
                acc += L1(p0 * acc);
                acc += L2(p0);
                static int L2(int a) => a;
                break;
        }
        acc /= 3;
        return acc;
    }
}
