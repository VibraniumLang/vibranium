WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vm2(2);
    long w3 = 0L;
    WriteLine(w3);
    return 0;
}
static int Len1(Span<int> x) => x.Length;
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); int k = Len1(s = b.AsSpan()); k += s.Length; return k; }
