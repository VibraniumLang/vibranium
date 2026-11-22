// vib-conformance: file-name Program.g.cs
WriteLine(C.M(-1, true));
WriteLine(C.M(0, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        if (Find1(2).IsNone(out var v2)) return acc;
        acc += v2;
        acc += acc;
        acc += Vm6(acc);
        return acc;
    }
    public static Option<int> Find1(int k) => k < 0 ? Some(k * 2) : None;
    public static int Vm4(int p) { Vec<int> b = [p, 2, 3]; Span<int> s = b.AsSpan(); int n = 0; Span<int> t = s; n += t.Length; b[0] = 1; n += s[0]; return n; }
    public static int Vm6(int p) { Vec<int> b = [p, 2, 3]; Span<int> s = b.AsSpan(); int n = 0; Span<int> t = s; n += s[0]; n += t[..2].Length; return n; }
}
