WriteLine(F(4, true, -2L));
WriteLine(F(1, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += p0;
    var ok1 = int.TryParse("42", out _);
    WriteLine(ok1);
    ref int r3 = ref acc;
    acc += M2(ref r3, 3) + (p1 ? acc : M2(r3, p0));
    WriteLine(r3);
    return acc;
}
static int M2(in int p, int k) { return p * 3; }
