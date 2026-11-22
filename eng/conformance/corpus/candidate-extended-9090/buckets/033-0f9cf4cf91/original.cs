WriteLine(F(-2, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(acc);
    foreach (var a2_e0 in a2.Items) WriteLine(a2_e0);
    acc += a2.Sum();
    a2.Add(p0);
    WriteLine((p1 ? acc : 1) - ((int)'c'));
    Feed3(ref a2, 1);
    acc += a2.Total + a2.Items.Count;
    acc += M4(ref acc, 3);
    WriteLine(acc);
    return acc;
}
static void Feed3(ref Acc1 x, int d) { x.Add(d); }
static int M4(in int p, int k) { return p * 3; }
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
