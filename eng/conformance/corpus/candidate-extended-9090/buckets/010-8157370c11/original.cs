WriteLine(F(3, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var a2 = new Acc1(p0);
    a2.Add(a2.Total);
    a2.Add(8);
    static int L3(int _)
    {
        {
            int.TryParse("33", out _);
        }
        WriteLine(_);
        _ += _;
        WriteLine(_);
        long x4 = (long)(_ != 5 ? 3 : _);
        return _;
    }
    acc += L3(p2 switch { < 2L when true => -1, >= 2L => acc });
    int x5 = acc % 2;
    acc += a2.AddTwice(1);
    acc += a2.Total + a2.Items.Count;
    double d6 = (double)x5;
    return acc;
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
}
