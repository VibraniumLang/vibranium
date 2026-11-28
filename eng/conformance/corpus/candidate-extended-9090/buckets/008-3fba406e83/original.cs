WriteLine(C.M(1, true, -2L));
WriteLine(C.M(-2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        Span<int> s1 = [2, 14, -5, 18, 14, 7];
        ReadOnlySpan<int> t2_r0 = s1.Slice(3, 3);
        acc += t2_r0.Length + (t2_r0.IsEmpty ? 0 : t2_r0[^1]);
        foreach (ref var t2_e1 in s1) t2_e1 += 3;
        ReadOnlySpan<int> t2_r2 = s1.Slice(1);
        acc += t2_r2.Length + (t2_r2.IsEmpty ? 0 : t2_r2[^1]);
        ReadOnlySpan<int> t2_ro3 = s1;
        acc += t2_ro3[0] + t2_ro3.Length;
        s1[0] = 7;
        if (p0 is > -2 and var q3 && q3 != 5)
        {
            acc += q3;
            acc = q3;
        }
        return acc;
    }
}
