WriteLine(C.M(6, true, -2L));
WriteLine(C.M(3, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        C1 c4 = new C1(p0);
        acc += c4.Run();
        int? nq = acc > 0 ? acc : null;
        WriteLine(nq ?? 0);
        int x5 = acc;
        return acc;
    }
}
public enum E { A = 3, B = 5, C = 9 }
public sealed class C1(int seed)
{
    public int _ = seed;
    public int Run()
    {
        WriteLine(5u);
        static int L3(int a) => (a <= 0 ? a : a + L3(a - 1)) + (int)E.C;
        WriteLine(L3(3));
        return _;
    }
}
