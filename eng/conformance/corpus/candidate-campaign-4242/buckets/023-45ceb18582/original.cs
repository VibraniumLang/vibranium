WriteLine(F(0, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    using (var r1 = new Res(6))
    {
        WriteLine(r1.Id);
        var x3 = (2 + p0) - (p0 / 7);
        acc += L2(p0);
        static int L2(int a)
        {
            WriteLine(!(a is > 2 and < 3));
            return a <= 0 ? a : a + L2(a - 1);
        }
    }
    long x4 = (long)p0;
    return acc;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
