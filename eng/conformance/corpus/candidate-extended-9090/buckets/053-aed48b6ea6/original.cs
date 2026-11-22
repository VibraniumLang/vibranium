WriteLine(F(6, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(-2 << p0);
    var q4 = new Ctr3();
    q4.Inc();
    q4.Inc();
    acc += q4.Get();
    foreach (var a2_e1 in a2.Items) WriteLine(a2_e1);
    acc += (int)((ushort)p0);
    var h6 = new Hold5(new Acc1(4));
    h6.Push(3);
    acc += h6.In.Total + h6.In.Sum();
    acc += a2.Total + a2.Items.Count;
    acc += M7(ref acc, 5);
    WriteLine(acc);
    return acc;
}
static int M7(in int p, int k) { return p * 2; }
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
}
public struct Ctr3
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
public sealed class Hold5(Acc1 inner)
{
    public Acc1 In = inner;
    [Mut] public void Push(int x) { In.Add(x); }
}
