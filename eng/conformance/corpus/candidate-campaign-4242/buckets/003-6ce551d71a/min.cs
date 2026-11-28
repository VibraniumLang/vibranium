WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    int _ = 0;
    Res r1 = new Res(0);
    acc += Eat2(r1);
    int s3 = 0 switch { < 0 when true => 0, >= 0 => 0 };
    WriteLine(_);
    return 0;
}
static int Eat2(Res x) { return 0; }
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
