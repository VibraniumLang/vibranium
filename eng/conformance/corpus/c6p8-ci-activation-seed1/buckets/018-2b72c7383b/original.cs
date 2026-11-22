WriteLine(F(5, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += Vm1(5);
    if (p0 is var q2 && q2 > -1)
    {
        acc += q2;
        acc += q2 ^ acc;
    }
    acc += M3(ref acc, 4);
    WriteLine(acc);
    return acc;
}
static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); Span<int> t = s = b.AsSpan(); int k = t.Length; k += s.Length; return k; }
static int M3(ref int p, int k)
{
    p += k;
    return p * 3;
}
