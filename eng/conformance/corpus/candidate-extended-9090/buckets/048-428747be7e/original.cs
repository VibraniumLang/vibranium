WriteLine(F(3, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [9, 10, 13];
    Span<int> s1 = v2.AsSpan();
    Span<int> t3_0 = s1[1..];
    Vec<int> v5 = [acc, 5, 5];
    foreach (ref var e4 in v5)
    {
        e4 += 1;
        if (p1)
        {
            double d6 = (double)(p0 | p0);
        }
        S s8 = new S();
        acc += M7(ref s8, 4);
        if (!p1) break;
        acc += e4;
    }
    if (t3_0.Length > 0) t3_0[0] = (int)4.5f;
    foreach (var t3_e0 in t3_0) acc += t3_e0;
    s1[1] += 6;
    acc += s1[1];
    foreach (ref var t3_e2 in s1) t3_e2 += 3;
    acc += v2.Count + v2[0];
    return acc;
}
static int M7(in S p, int k) { return p.A * 2; }
public struct S
{
    public int A;
    public int B;
}
