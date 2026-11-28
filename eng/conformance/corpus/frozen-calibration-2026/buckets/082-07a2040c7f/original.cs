WriteLine(F(-1, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    {
        acc += (p1 ? p0 : 6) << acc;
        var hold = new Holder(new Res(3));
        WriteLine(hold.R.Id);
        int L1(int a) => a;
        acc += L1(acc + acc);
    }
    return acc;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
public sealed class Holder(Res r) { public Res R = r; }
