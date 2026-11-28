WriteLine(F(6, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    int _ = 4;
    Res r1 = new Res(2);
    acc += -1;
    acc += Eat2(r1);
    int s3 = _ switch { < 2 when true => _, >= 2 => 8 };
    acc += s3;
    WriteLine(_);
    int x4 = s3;
    return acc;
}
static int Eat2(Res x)
{
    using var _ = x;
    return x.Id;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
