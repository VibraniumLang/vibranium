WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [15, 7, -1, 7, 10];
    Span<int> s1 = v2.Slice(0, 5);
    foreach (ref var t3_e1 in s1) t3_e1 += 2;
    Span<int> t3_2 = s1[3..6];
    static int L4(int a) { return 0; }
    acc += L4(0);
    return 0;
}
