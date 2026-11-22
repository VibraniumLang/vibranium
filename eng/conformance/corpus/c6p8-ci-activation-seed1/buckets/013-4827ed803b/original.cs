WriteLine(C.M(3, true));
WriteLine(C.M(5, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += (int)(p1 ? 4.25 : 7.25);
        acc += Try4(acc).Unwrap();
        acc += Vw3(5);
        acc += Vm1(p0 + acc);
        return acc;
    }
    public static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; Span<int> s = a.AsSpan(); int k = (s = b.AsSpan())[1..].Length; k += s.Length; return k; }
    public static int Vw3(int p) { var g = new Gv2([p, 1]); Vec<int> b = [3]; var s = g.Get(b.AsReadOnlySpan()); b.Add(9); return s[0]; }
    public static Result<int, int> Try4(int k) => k > 2 ? (Result<int, int>)Ok(k) : Err(3 - k);
}
public sealed class Gv2(Vec<int> v) { Vec<int> items = v; public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => items.AsReadOnlySpan(); }
