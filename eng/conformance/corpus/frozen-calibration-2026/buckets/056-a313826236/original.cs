WriteLine(F(-1, true, -2L));
WriteLine(F(8, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    WriteLine(p2);
    S s2 = new S();
    acc += M1(ref s2, 2);
    {
        using var dsp = new Dsp();
        WriteLine(1);
    }
    double w3 = (double)(p2 + 5);
    WriteLine((int)(w3 % (w3 + w3)));
    return acc;
}
static int M1(ref S p, int k)
{
    p.A += k;
    return p.A * 1;
}
public struct S
{
    public int A;
    public int B;
}
public struct Dsp : IDisposable { public int X; public void Dispose() => WriteLine(X); }
