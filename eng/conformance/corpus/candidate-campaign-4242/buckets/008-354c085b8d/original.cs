WriteLine(F(3, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var x1 = (long)acc;
    acc += M2(out acc, 5) + (p0 < 7 ? acc : acc);
    WriteLine(acc);
    ReadOnlySpan<char> sp3 = (p0 < 7) ? ['f', 'c'] : ['c', 'b'];
    acc += (int)sp3[0];
    return acc;
}
static int M2(out int p, int k)
{
    p = k;
    return p * 2;
}
