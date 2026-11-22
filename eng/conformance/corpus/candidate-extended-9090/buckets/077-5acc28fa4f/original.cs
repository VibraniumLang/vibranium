WriteLine(C.M(-1, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        static int L1(int _)
        {
            WriteLine(_);
            int.TryParse("48", out _);
            WriteLine(_);
            var x2 = _;
            return _;
        }
        acc += L1(p0);
        Span<int> s3 = [12, -5, 4, 10, 3, 0];
        ReadOnlySpan<int> t4_ro0 = s3;
        acc += t4_ro0[0] + t4_ro0.Length;
        s3[0] = 3;
        Span<int> t4_1 = s3[1..1];
        acc += (acc - -3) % ((int)(ushort)9);
        if (acc is > 2 and < 3) VibraniumRuntime.Panic("boom");
        acc += t4_1.Length;
        return acc;
    }
}
