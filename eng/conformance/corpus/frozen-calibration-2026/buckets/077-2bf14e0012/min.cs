WriteLine(F(0, false));
int F(int p0, bool p1)
{
    int acc = 0;
    var x1 = 0;
    bool ok3 = TryGet2((int)(ushort)x1, out _);
    return acc;
}
static bool TryGet2(int k, out int v)
{
    v = 0;
    return false;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
public sealed class Holder(Res r) { public Res R = r; }
