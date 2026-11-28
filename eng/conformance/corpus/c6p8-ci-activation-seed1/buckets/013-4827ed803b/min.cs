WriteLine(0);
public static class C
{
    public static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); int k = (s = b.AsSpan())[1..].Length; k += s.Length; return k; }
}
