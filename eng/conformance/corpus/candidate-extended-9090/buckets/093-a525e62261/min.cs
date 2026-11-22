WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var a2 = new Acc1(p0);
    Feed3(ref a2, 1);
    Feed4(ref a2, 4);
    int K5 = 0;
    switch (0)
    {
        case K5:
        default: break;
    }
    return acc;
}
static void Feed3(ref Acc1 x, int d) { x.Add(d); }
static void Feed4(ref Acc1 x, int d) { x.Add(d); }
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
}
