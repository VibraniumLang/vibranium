WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> v2 = [];
    foreach (var e1 in v2)
    {
        var a4 = new Acc3(e1 * acc);
        acc += Look5(in a4);
        acc += v2.Length;
        var q7 = new Ctr6();
        acc += q7.Get();
    }
    return 0;
}
static int Look5(in Acc3 x) => x.Sum() + x.Total;
public sealed class Acc3(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
}
public struct Ctr6
{
    public int N;
    public readonly int Get() => N;
}
