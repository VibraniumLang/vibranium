WriteLine(F(-2, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var d1 = (double)(acc - acc);
    S s3 = new S();
    acc += M2(ref s3, 5) + (1 <= p0 ? p0 : acc % acc);
    {
        using var dsp = new Dsp();
        WriteLine(1);
    }
    return acc;
}
static int M2(ref S p, int k)
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
