WriteLine(C.M(0, true, -2L));
WriteLine(C.M(2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        var c5 = new C1();
        acc += c5.Run();
        return acc;
    }
}
public sealed class C1
{
    public int _ = 7;
    public int Run()
    {
        static int L2(int a)
        {
            WriteLine((uint)a);
            return a <= 0 ? a : a + L2(a - 1);
        }
        ushort x3 = (ushort)(_);
        WriteLine(L2(_ | _));
        var s4 = _ switch { < 1 when true => _, >= 1 => _ > 4 ? _ / 2 : _ };
        WriteLine(s4);
        int.TryParse("34", out _);
        return _;
    }
}
