WriteLine(F(2, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    int x1 = acc;
    int r2;
    switch (x1)
    {
        case < 0: long x3 = (long)p0; r2 = -1; break;
        case >= 0: r2 = (acc >> x1) << 7; break;
    }
    acc += r2;
    var md = new Res(5);
    md.Dispose();
    return acc;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
