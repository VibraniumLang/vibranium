WriteLine(F(1, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Vm2(2);
    WriteLine(p1);
    long w3 = (long)(acc + 7);
    WriteLine(w3 % 7L);
    return acc;
}
static int Len1(Span<int> x) => x.Length;
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); int k = Len1(s = b.AsSpan()); k += s.Length; return k; }
