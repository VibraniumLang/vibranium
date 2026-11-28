WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Vm3(p0);
    acc += Vm2(acc);
    return 0;
}
static Span<int> Id1(Span<int> x) => x;
static int Vm2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { Span<int> s = b.AsSpan(); t = Id1(s); n += t[1..].Length; } n += t.Length; return n; }
static int Vm3(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) { n += (s = b.AsSpan())[1..].Length; s = a.AsSpan(); n += s.Length; } return n; }
