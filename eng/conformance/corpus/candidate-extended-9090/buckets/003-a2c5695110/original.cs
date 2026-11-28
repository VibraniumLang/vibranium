WriteLine(F(2, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [15, 7, -1, 7, 10];
    Span<int> s1 = v2.Slice(0, 5);
    ReadOnlySpan<int> t3_r0 = s1.Slice(2);
    acc += t3_r0.Length + (t3_r0.IsEmpty ? 0 : t3_r0[^1]);
    foreach (ref var t3_e1 in s1) t3_e1 += 2;
    Span<int> t3_2 = s1[3..6];
    static int L4(int a)
    {
        WriteLine(a);
        return a;
    }
    if (!(p0 == 1))
    {
        double d5 = (double)p0;
    }
    acc += L4(acc);
    foreach (var t3_e2 in t3_2) acc += t3_e2;
    acc += v2.Count + v2[0];
    return acc;
}
