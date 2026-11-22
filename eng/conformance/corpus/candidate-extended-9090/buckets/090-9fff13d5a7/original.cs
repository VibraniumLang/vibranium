WriteLine(F(3, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [p0];
    foreach (var e1 in v2)
    {
        var a4 = new Acc3(e1 * acc);
        acc += Look5(in a4);
        acc += v2.Length;
        a4.Add(a4.Total);
        var q7 = new Ctr6();
        q7.Inc();
        q7.Inc();
        acc += q7.Get();
        acc += a4.Total + a4.Items.Count;
        if (p1) break;
        acc += e1;
    }
    long x8 = (long)p0;
    return acc;
}
static int Look5(in Acc3 x) => x.Sum() + x.Total;
public sealed class Acc3(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
}
public struct Ctr6
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
