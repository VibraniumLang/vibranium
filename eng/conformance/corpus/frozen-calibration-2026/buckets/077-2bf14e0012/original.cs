WriteLine(F(1, true));
WriteLine(F(1, false));
int F(int p0, bool p1)
{
    int acc = p0;
    var x1 = p0 + 4;
    bool ok3 = TryGet2((int)((ushort)(x1 + acc)), out _);
    WriteLine(ok3);
    var hold = new Holder(new Res(3));
    WriteLine(hold.R.Id);
    return acc;
}
static bool TryGet2(int k, out int v)
{
    v = k * 3;
    return k > 0;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
public sealed class Holder(Res r) { public Res R = r; }
