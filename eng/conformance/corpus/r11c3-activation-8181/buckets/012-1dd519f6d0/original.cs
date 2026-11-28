// vib-conformance: file-name Program.g.cs
WriteLine(C.M(6, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Ct3(p0);
        var d7 = (double)(6L >> p0);
        acc += Ct6(p0);
        WriteLine(p0);
        if (p1)
        {
            VibraniumRuntime.Panic("boom");
            return acc;
        }
        return acc;
    }
    public static Vec<int> MakeV1() => [1, 2, 3];
    public static int Len2(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct3(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV1().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len2(v.AsReadOnlySpan()); }
    public static Vec<int> MakeV4() => [1, 2, 3];
    public static int Len5(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct6(int p) { Vec<int> v = [p]; bool c = p > 0; return Len5(c ? v.AsReadOnlySpan() : MakeV4().AsReadOnlySpan()) + Len5(!c ? v.AsReadOnlySpan() : MakeV4().AsReadOnlySpan()); }
}
