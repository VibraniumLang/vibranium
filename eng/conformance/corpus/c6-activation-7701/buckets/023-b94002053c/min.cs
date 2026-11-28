WriteLine(0);
public static class C
{
    public static Vec<int> MakeV1() => [1, 2, 3];
    public static int Len2(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct3(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV1().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len2(v.AsReadOnlySpan()); }
    public static ReadOnlySpan<int> Sx4(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
}
public sealed class C6(int seed)
{
    public int F = seed;
    public int Run()
    {
        int.TryParse("35", out F);
        return 0;
    }
}
