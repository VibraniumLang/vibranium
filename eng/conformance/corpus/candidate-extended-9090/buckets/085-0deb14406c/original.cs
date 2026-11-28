WriteLine(F(1, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(p0 / 4);
    a2.Add(a2.Total);
    foreach (var a2_e1 in a2.Items) WriteLine(a2_e1);
    Span<int> v4 = [6, p0];
    foreach (var e3 in v4)
    {
        acc += v4.Length / 6;
        if (e3 is <= 3) continue;
        if ((p0 + e3) == acc)
        {
            int x5 = e3 + (int)-3.25;
        }
        else
        {
            ushort x6 = (ushort)(e3);
        }
        acc += e3;
    }
    acc += a2.Total + a2.Items.Count;
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
