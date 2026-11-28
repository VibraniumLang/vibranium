WriteLine(F(6, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += acc % 1;
    acc += Vm2(3);
    var s3 = (double)p0 switch { 2.25 => p1 || (acc < 2) ? (int)(ushort)8 : acc, _ => p0 };
    acc += s3;
    acc += Vm1(-3);
    WriteLine(2u * ((uint)8L));
    return acc;
}
static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { n += t[0]; Span<int> s = b.AsSpan(); t = s; a.Add(i); } return n + t.Length + a.Count; }
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; ReadOnlySpan<int> s = a.AsReadOnlySpan(); int k = (s = b.AsReadOnlySpan()).Length; k += s.Length; return k; }
