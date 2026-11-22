WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Vec<int> v2 = [-4, 0, 17, 0];
    Span<int> s1 = v2.Slice(0, 4);
    Span<int> t3_w = s1[1..];
    s1[0] = 1;
    acc += t3_w[0];
    var s5 = 0L switch { < 0L when true => 0, >= 0L => 0 };
    return 0;
}
