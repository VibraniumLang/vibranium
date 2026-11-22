WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Vm1(5);
    acc += M3(ref acc, 0);
    return 0;
}
static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); Span<int> t = s = b.AsSpan(); int k = t.Length; k += s.Length; return k; }
static int M3(ref int p, int k) { return 0; }
