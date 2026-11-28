WriteLine(F(-2, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += new Zc1(1).K;
    var x2 = acc + -3;
    S s4 = new S();
    TryGet3((int)-3.25, out s4.B);
    return acc;
}
static bool TryGet3(int k, out int v)
{
    v = k * 3;
    return k > 0;
}
public sealed class Zc1 { public Vec<int> Items; public int K; public Zc1(int k) { K = Peek(); Items = [k]; } int Peek() => Items.Count; }
public struct S
{
    public int A;
    public int B;
}
