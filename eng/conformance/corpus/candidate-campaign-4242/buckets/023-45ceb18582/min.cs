WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    using (var r1 = new Res(0))
    {
        static int L2(int a)
        {
            WriteLine(0 is > 0 and < 0);
            return L2(0);
        }
    }
    return acc;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
