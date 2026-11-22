WriteLine(F(3, true, -2L));
WriteLine(F(5, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Vec<int> v2 = [-4, 0, 17, 0];
    Span<int> s1 = v2.Slice(0, 4);
    s1[0] += acc;
    acc += s1[0];
    acc += s1[^1] + s1[^4];
    s1[1] = (int)8.25;
    acc += s1[1];
    double d4 = p1 ? 1.25 : (double)-2;
    Span<int> t3_w = s1[1..];
    s1[0] = 1;
    acc += t3_w[0];
    acc += v2.Count + v2[0];
    var s5 = p2 switch { < 1L when true => acc, >= 1L => p0 };
    acc += s5;
    return acc;
}
