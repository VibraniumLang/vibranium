WriteLine(C.M(-2, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vw2(0);
        var x3 = p0 - 9;
        int w4 = (int)(acc + 9);
        w4 %= acc;
        WriteLine(w4);
        return acc;
    }
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = []; return x; }
    public static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s.Length + a.Count; }
}
