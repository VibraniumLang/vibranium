WriteLine(F(-1, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    {
        using Res r1 = new Res(2);
        WriteLine(r1.Id);
        if (0 == (acc + p0))
        {
            var x2 = p0 + -2;
        }
        if (p1) return p0;
    }
    acc += M3(out acc, 3) + (acc != 0 ? p0 : acc);
    WriteLine(acc);
    return acc;
}
static int M3(out int p, int k)
{
    p = k;
    return p * 1;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
