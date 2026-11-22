WriteLine(F(0, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    C1 c5 = new C1(acc % (p0 > 0 ? p0 : p0));
    acc += c5.Run();
    return acc;
}
public enum E { A = 1, B = 2, C = 7 }
public struct SO(int a, int b, Vec<int> n)
{
    public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
public sealed class C1(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        int r2;
        switch (E.C)
        {
            case E.A: SO s3; s3.A = F; double d4 = (double)F; WriteLine(s3.A); r2 = F + 2; break;
            case E.B: r2 = (int)E.C; break;
            default: r2 = F; break;
        }
        WriteLine(r2);
        return F;
    }
}
