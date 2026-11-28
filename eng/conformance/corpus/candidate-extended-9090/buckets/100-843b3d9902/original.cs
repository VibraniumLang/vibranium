WriteLine(F(2, true, -2L));
WriteLine(F(-3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var a2 = new Acc1(6);
    var q4 = new Ctr3();
    q4.Inc();
    q4.Inc();
    acc += q4.Get();
    foreach (var a2_e1 in a2.Items) WriteLine(a2_e1);
    acc += p0;
    nint w5 = (nint)(acc + 1);
    WriteLine(w5 / (nint)-2);
    var h7 = new Hold6(new Acc1(0));
    h7.Push(9);
    acc += h7.In.Total + h7.In.Sum();
    acc += a2.Total + a2.Items.Count;
    WriteLine(p0);
    return acc;
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
public struct Ctr3
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
public sealed class Hold6(Acc1 inner)
{
    public Acc1 In = inner;
    [Mut] public void Push(int x) { In.Add(x); }
}
