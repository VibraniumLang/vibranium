WriteLine(C.M(6, true));
WriteLine(C.M(2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Ca2(p0 > 1 ? acc & 7 : 9);
        acc *= p0;
        acc += Vw4(acc);
        bool ok5 = int.TryParse("x", out int _);
        WriteLine(ok5);
        if (acc is 5)
        {
            acc += p0 != 1 ? acc : 4 | p0;
        }
        else
        {
            acc += (p0 / 6) % acc;
        }
        return acc;
    }
    public static int Ca2(int p) { Vec<int> x = [p, 2]; var c = new Ck1(p); int r = c.Eat(Move(x), in x); return r; }
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
    public static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan()); return s[0] + s.Length; }
}
public sealed class Ck1(int z) { public int Z = z; public int Eat(Vec<int> b, in Vec<int> a) { int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
