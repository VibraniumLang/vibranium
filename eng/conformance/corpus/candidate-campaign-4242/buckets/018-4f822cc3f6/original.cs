WriteLine(F(0, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += p0;
    acc += M1(ref acc, 5);
    WriteLine(acc);
    int s2 = (3.25 + 3.25) % 1.25 switch { 2.25 => 7, > 0.25 and < 6.25 when p1 => 8 - p0, _ => 8 };
    acc += s2;
    return acc;
}
static int M1(ref int p, int k)
{
    p += k;
    return p * 1;
}
