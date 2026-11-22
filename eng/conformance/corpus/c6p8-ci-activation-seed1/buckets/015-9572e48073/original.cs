WriteLine(F(2, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    {
        using var r2 = new Res(5);
        using var r3 = new Res(2);
        if (acc is not (-1 or 7))
        {
            long x4 = p2;
        }
        WriteLine(r2.Id - r3.Id);
    }
    acc += Vm1((int)'e');
    return acc;
}
static int Vm1(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4, 5]; int n = 0; Span<int> t = a.AsSpan(); for (int i = 0; i < 2; i++) { Span<int> s = b.AsSpan(); t = s; n += t[0]; } n += t.Length; return n; }
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
