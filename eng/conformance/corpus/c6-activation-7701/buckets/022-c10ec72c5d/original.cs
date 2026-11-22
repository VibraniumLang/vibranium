WriteLine(F(4, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var r1 = new Res(8);
    acc += Vw4(acc < p0 ? p0 : p0);
    bool b5 = p0 < 5;
    acc += Eat2(r1);
    return acc;
}
static int Eat2(Res x)
{
    using var g = x;
    return x.Id;
}
static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan()); return s[0] + s.Length; }
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
