WriteLine(C.M(5, true));
WriteLine(C.M(3, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        {
            C2 c4 = new C2();
            acc += c4.Run();
            ReadOnlySpan<char> sp5 = (p1 || p1)?['c', 'f']:['f', 'd', 'b'];
            acc += (int)sp5[0];
            static int L1(int a) => -3;
            acc += L1(acc);
        }
        return acc;
    }
}
public sealed class C2
{
    public int F = 9;
    [Mut] public int Run()
    {
        double d3 = (double)F;
        return F;
    }
}
