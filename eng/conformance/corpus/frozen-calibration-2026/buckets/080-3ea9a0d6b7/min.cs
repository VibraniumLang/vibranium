WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    if (acc > 100) goto Skip0;
    Skip0: WriteLine(0);
    return 0;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
public sealed class Holder(Res r) { public Res R = r; }
