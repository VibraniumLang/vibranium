WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var r1 = new Res(0);
    acc += Vw4(acc < p0 ? p0 : p0);
    acc += Eat2(r1);
    return 0;
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
    public void Dispose() => WriteLine(0);
}
