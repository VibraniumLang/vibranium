WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v2 = [-3, -5, 10, -2, 18];
        Span<int> s1 = v2[..];
        foreach (ref var t3_e1 in s1) t3_e1 += 2;
        v2.Add(1);
        acc += s1[0];
        acc += M8(ref acc, 0);
        return 0;
    }
    public static int M8(in int p, int k) { return 0; }
}
