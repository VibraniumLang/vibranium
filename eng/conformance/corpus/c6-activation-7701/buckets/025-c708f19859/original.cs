WriteLine(C.M(-2, true));
WriteLine(C.M(9, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        WriteLine(unchecked((uint)(2e19)));
        WriteLine(unchecked((int)(1e6)));
        acc += Vw2(-1);
        WriteLine(p1);
        acc += Vw4(-3);
        return acc;
    }
    public static int Vw2(int p) { var g = new Gv1([p, 1]); Vec<int> b = [3]; var s = g.Get(b.AsReadOnlySpan()); b.Add(9); return s[0]; }
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
    public static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan()); return s[0] + s.Length; }
}
public sealed class Gv1(Vec<int> v) { Vec<int> items = v; public ReadOnlySpan<int> Get(ReadOnlySpan<int> other) => other; }
