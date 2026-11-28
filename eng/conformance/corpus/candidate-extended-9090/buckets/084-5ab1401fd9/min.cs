WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [3, -1, 4];
    S s6;
    acc += M5(ref s6, 0);
    foreach (ref var t2_e2 in s1) t2_e2 += 1;
    acc += s1[^1] + s1[^3];
    return 0;
}
static int M5(in S p, int k) { return 0; }
public struct S
{
}
