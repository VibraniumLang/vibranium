WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [4, 15, -5, 5, 18];
    foreach (ref var t2_e0 in s1) t2_e0 += 2;
    acc += Over7(s1, s1);
    return 0;
}
static int Over7(Span<int> a, Span<int> b) => a.Length + b.Length;
