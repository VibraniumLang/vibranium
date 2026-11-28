WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s1 = [17, 5, 11, 10, -5, 10];
    foreach (ref var t2_e0 in s1) t2_e0 += 2;
    Bump3(s1[5..6], 4);
    int s5 = 0 switch { < 0 => 0, >= 0 when true => 0 };
    return acc;
}
static void Bump3(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
