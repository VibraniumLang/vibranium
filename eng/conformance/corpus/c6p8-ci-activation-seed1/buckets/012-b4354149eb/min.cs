WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    double d1 = 0.0;
    WriteLine(unchecked((ulong)d1));
    acc += Vm2(acc);
    TryGet3(0, out int _);
    return 0;
}
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) { n += (s = b.AsSpan())[0]; } return n; }
static bool TryGet3(int k, out int v)
{
    v = 0;
    return false;
}
