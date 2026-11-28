WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Ca3(acc);
    WriteLine(unchecked((nint)(1e19)));
    return 0;
}
static int Ca3(int p) { var x = new Rs1(p); var y = new Rs1(p + 1); int r = new Cw2(in x, Move(y)).K; using var d = x; return r; }
public sealed class Rs1(int id) : IDisposable { public int Id = id; public void Dispose() { WriteLine(Id); } }
public sealed class Cw2 { public int K; public Cw2(in Rs1 a, Rs1 b) { int t = Sink(b); K = a.Id + t; } static int Sink(Rs1 x) { int k = x.Id; using var d = x; return k; } }
