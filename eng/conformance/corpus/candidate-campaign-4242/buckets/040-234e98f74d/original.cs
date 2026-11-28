WriteLine(F(5, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [p0];
    foreach (ref var e1 in v2)
    {
        e1 += 1;
        static int L3(int a) => a;
        acc += acc;
        acc += L3(v2.Length);
        acc += L4(p0);
        static int L4(int a) => (int)(4.5f + 4.5f);
        acc += e1;
    }
    return acc;
}
