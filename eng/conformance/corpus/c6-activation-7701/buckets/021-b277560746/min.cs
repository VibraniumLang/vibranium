WriteLine(0);
public static class C
{
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
    public static Vec<int> MakeV3() => [1, 2, 3];
    public static int Len4(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct5(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV3().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len4(v.AsReadOnlySpan()); }
}
