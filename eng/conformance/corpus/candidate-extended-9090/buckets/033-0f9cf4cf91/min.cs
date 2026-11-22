WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var a2 = new Acc1(acc);
    Feed3(ref a2, 1);
    acc += M4(ref acc, 0);
    return 0;
}
static void Feed3(ref Acc1 x, int d) { x.Add(d); }
static int M4(in int p, int k) { return 0; }
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
}
