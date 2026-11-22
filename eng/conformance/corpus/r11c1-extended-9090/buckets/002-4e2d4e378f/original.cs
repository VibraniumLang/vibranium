WriteLine(F(6, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    switch (acc)
    {
        case < 5: int.TryParse("2", out var o1); acc += o1; return acc;
        case >= 5:
            o1 = (int)((char)(acc >> 4));
            acc += o1;
            int.TryParse("4", out var o2);
            acc += o2;
            return p0;
        default:
            o1 = acc;
            acc += o1;
            Span<int> s3 = [5, 17, 13];
            ReadOnlySpan<int> t4_ro0 = s3;
            acc += t4_ro0[0] + t4_ro0.Length;
            s3[0] = 3;
            ReadOnlySpan<int> t4_r1 = s3.Slice(0);
            acc += t4_r1.Length + (t4_r1.IsEmpty ? 0 : t4_r1[^1]);
            double d5 = (double)p0;
            Span<int> t4_w = s3;
            acc += s3[0];
            int x6 = acc;
            return acc;
    }
}
