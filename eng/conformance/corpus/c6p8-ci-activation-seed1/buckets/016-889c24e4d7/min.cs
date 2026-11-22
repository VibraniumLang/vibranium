WriteLine(0);
public static class C
{
    public static int Len2(Span<int> x) => x.Length;
    public static int Vm3(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> s = a.AsSpan(); for (int i = 0; i < 2; i++) { n += Len2(s = b.AsSpan()); } return n; }
}
