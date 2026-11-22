WriteLine(F(3, true, -2L));
WriteLine(F(3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += acc ^ p0;
    var s1 = 6.25 % (3.25 - -3.25) switch { < -1.25 => acc, 3.25 when 1.25 < 7.25 => p0, _ => 3 };
    acc += s1;
    acc += P2(5.25 >= -3.25 ? p2 : p2 / 4L, s1);
    return acc;
}
static int P2(long c0, int c1) => (int)c0 + c1;
