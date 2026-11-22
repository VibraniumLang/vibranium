WriteLine(C.M(-2, true, -2L));
WriteLine(C.M(0, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        static int L1(int _)
        {
            {
                int.TryParse("58", out var _);
            }
            WriteLine(_);
            var x2 = _;
            return _;
        }
        acc += L1((int)p2);
        double d3 = (double)acc;
        acc += (int)G4(acc)[0];
        acc += (int)E.C;
        return acc;
    }
    public static ReadOnlySpan<E> G4(int k)
    {
        VibraniumRuntime.Panic("boom");
        return [E.C, E.A];
    }
}
public enum E { A = 3, B = 5, C = 7 }
