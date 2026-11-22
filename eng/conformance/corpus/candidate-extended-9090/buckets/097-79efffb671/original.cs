WriteLine(F(0, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(7);
    foreach (var a2_e0 in a2.Items) WriteLine(a2_e0);
    acc += a2.Drain();
    var _ = acc;
    var b3 = !(p0 is > 2 and < 3);
    switch (b3)
    {
        case true: acc += acc; break;
        case false: break;
    }
    int.TryParse("36", out _);
    WriteLine(_);
    _ = p0;
    WriteLine(_);
    _ -= 3;
    WriteLine(_);
    var h5 = new Hold4(new Acc1(3));
    h5.Push(7);
    acc += h5.In.Total + h5.In.Sum();
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
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
public sealed class Hold4(Acc1 inner)
{
    public Acc1 In = inner;
    [Mut] public void Push(int x) { In.Add(x); }
}
