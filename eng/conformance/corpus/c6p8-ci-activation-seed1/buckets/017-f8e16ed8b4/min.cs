WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vm2(3);
    var s3 = 0;
    acc += s3;
    acc += Vm1(-3);
    return 0;
}
static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { n += t[0]; Span<int> s = b.AsSpan(); t = s; a.Add(i); } return n + t.Length + a.Count; }
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; ReadOnlySpan<int> s = a.AsReadOnlySpan(); int k = (s = b.AsReadOnlySpan()).Length; k += s.Length; return k; }
