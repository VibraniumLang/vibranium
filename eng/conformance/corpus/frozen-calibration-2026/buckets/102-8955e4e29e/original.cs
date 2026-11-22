WriteLine(C.M(6, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        int L1(int a) => a;
        WriteLine(0L);
        acc += (int)G3(6)[0];
        acc += L1(p1 ? 7 : 0);
        acc += L2(7);
        static int L2(int a) => 4;
        return acc;
    }
    public static ReadOnlySpan<byte> G3(int k)
    {
        if (k < 100) VibraniumRuntime.Panic("boom");
        return [(byte)3, (byte)3];
    }
}
