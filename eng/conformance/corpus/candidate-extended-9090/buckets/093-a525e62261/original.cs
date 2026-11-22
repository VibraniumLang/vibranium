WriteLine(F(5, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(p0);
    Feed3(ref a2, 1);
    Feed4(ref a2, 4);
    int K5 = 6;
    acc += K5;
    switch (acc)
    {
        case K5: WriteLine(1); break;
        default: WriteLine(2); break;
    }
    var h7 = new Hold6(new Acc1(2));
    h7.Push(7);
    acc += h7.In.Total + h7.In.Sum();
    acc += a2.Total + a2.Items.Count;
    acc += p0;
    return acc;
}
static void Feed3(ref Acc1 x, int d) { x.Add(d); }
static void Feed4(ref Acc1 x, int d) { x.Add(d); }
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
public sealed class Hold6(Acc1 inner)
{
    public Acc1 In = inner;
    [Mut] public void Push(int x) { In.Add(x); }
}
