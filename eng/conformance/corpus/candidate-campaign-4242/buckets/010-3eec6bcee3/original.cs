WriteLine(C.M(0, true));
WriteLine(C.M(9, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        static int L1(int a) => a switch { < 3 => a, >= 3 when true => a };
        {
            using var r2 = new Res(7);
            using var r3 = new Res(4);
            acc /= 4;
            WriteLine(r2.Id - r3.Id);
        }
        acc += L1(p0);
        return acc;
    }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
