WriteLine(F(4, true, -2L));
WriteLine(F(6, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var a2 = new Acc1(p0 - (acc + acc));
    a2.Add(a2.Total);
    if ((0.25 != 1.25) && (acc < p0))
    {
        int x3 = p0;
    }
    else
    {
        bool b4 = acc is > 2 and < 3;
    }
    var q6 = new Ctr5();
    q6.Inc();
    q6.Inc();
    acc += q6.Get();
    a2.Add((int)9.25);
    acc += a2.Total + a2.Items.Count;
    var ok9 = TryGet7(acc, out var o8);
    WriteLine(ok9);
    WriteLine(o8);
    return acc;
}
static bool TryGet7(int k, out int v)
{
    v = k * 3;
    return k > 0;
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
}
public struct Ctr5
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
