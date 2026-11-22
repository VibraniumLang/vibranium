WriteLine(0);
public static class C
{
    public static int Vm6(int p) { Vec<int> b = [p, 2, 3]; Span<int> s = b.AsSpan(); int n = 0; Span<int> t = s; n += s[0]; n += t[..2].Length; return n; }
}
