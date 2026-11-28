WriteLine(C.M(5, true));
WriteLine(C.M(-1, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        switch (acc)
        {
            case 1 when acc is 2 or 3:
                {
                    var x6 = acc;
                    break;
                }
            case not 1 when acc < 3: acc += p0; break;
        }
        acc += Ct5(p0);
        acc += Vw2(p0 - acc);
        return acc;
    }
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
    public static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
    public static Vec<int> MakeV3() => [1, 2, 3];
    public static int Len4(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct5(int p) { bool c = p > 0; return Len4(c ? MakeV3().AsReadOnlySpan() : MakeV3().AsReadOnlySpan()[..1]); }
}
