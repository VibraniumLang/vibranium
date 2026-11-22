WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    using Res r3 = new Res(0);
    acc += Eat4(r3);
    acc += Vw2(p0);
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2, 3]; return x[1..]; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
static int Eat4(Res x)
{
    using var g = x;
    return x.Id;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
