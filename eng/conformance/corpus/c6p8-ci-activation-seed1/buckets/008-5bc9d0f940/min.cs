WriteLine(0);
public static class C
{
    public static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { Span<int> s = b.AsSpan(); t = s; n += t[0]; } n += t.Length; return n; }
}
