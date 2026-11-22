WriteLine(F(1, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [3, -1, 4];
    ReadOnlySpan<int> t2_ro0 = s1;
    acc += t2_ro0[0] + t2_ro0.Length;
    s1[0] = 7;
    Span<int> t2_1 = s1[1..];
    if (5L >= 1L)
    {
        bool b3 = p1;
    }
    else
    {
        double d4 = (double)p0;
    }
    S s6 = new S();
    acc += M5(ref s6, 2) + (p0 >= 1 ? -1 : acc);
    foreach (var t2_e1 in t2_1) acc += t2_e1;
    foreach (ref var t2_e2 in s1) t2_e2 += 1;
    acc += s1[^1] + s1[^3];
    return acc;
}
static int M5(in S p, int k) { return p.A * 2; }
public struct S
{
    public int A;
    public int B;
}
