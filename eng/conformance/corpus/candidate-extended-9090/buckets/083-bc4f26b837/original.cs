WriteLine(F(-1, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [17, 5, 11, 10, -5, 10];
    foreach (ref var t2_e0 in s1) t2_e0 += 2;
    Bump3(s1[5..6], 4);
    acc += s1[0];
    if ((p0 != acc) || (p0 is 4))
    {
        var x4 = acc;
    }
    int s5 = acc switch { < 3 => acc, >= 3 when true => acc };
    acc += s5;
    double d6 = (double)p0;
    return acc;
}
static void Bump3(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
