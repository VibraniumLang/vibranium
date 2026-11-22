// vib-conformance: file-name Program.g.cs
WriteLine(F(6, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    WriteLine(unchecked((ulong)double.NaN));
    double d1 = 0.0;
    WriteLine(unchecked((ulong)d1));
    WriteLine(unchecked((ulong)double.NaN));
    acc += (acc + 3) * p0;
    acc += Vm2(acc);
    TryGet3(9 / acc, out int _);
    return acc;
}
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) { n += (s = b.AsSpan())[0]; } return n; }
static bool TryGet3(int k, out int v)
{
    v = k * 2;
    return k > 0;
}
