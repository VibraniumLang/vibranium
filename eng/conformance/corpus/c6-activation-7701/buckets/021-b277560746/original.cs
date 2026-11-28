WriteLine(C.M(3, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vw2(p0 * -3);
        acc += Ct5(acc);
        long x6 = (long)p0;
        ReadOnlySpan<E> ca8 = [E.B, E.C, E.B];
        ReadOnlySpan<E> cb9 = [E.C];
        acc += First7(p1 ? ca8 : cb9);
        acc += acc;
        return acc;
    }
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
    public static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
    public static Vec<int> MakeV3() => [1, 2, 3];
    public static int Len4(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
    public static int Ct5(int p) { Vec<int> v = [p]; bool c = p > 0; ReadOnlySpan<int> s = c ? MakeV3().AsReadOnlySpan() : v.AsReadOnlySpan(); return s[0] + Len4(v.AsReadOnlySpan()); }
    public static int First7(ReadOnlySpan<E> q) => q.Length > 0 ? (int)q[0] : -1;
}
public enum E { A = 0, B = 1, C = 4 }
