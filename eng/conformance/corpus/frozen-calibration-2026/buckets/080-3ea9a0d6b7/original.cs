WriteLine(F(6, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += -2;
    if (acc > 100) goto Skip0;
    WriteLine(acc);
    Skip0: WriteLine(0);
    var hold = new Holder(new Res(3));
    WriteLine(hold.R.Id);
    return acc;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
public sealed class Holder(Res r) { public Res R = r; }
