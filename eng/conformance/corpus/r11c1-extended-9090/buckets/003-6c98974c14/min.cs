WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    byte x1 = (byte)0;
    int r2;
    switch (x1)
    {
        case < (byte)3: int x4; r2 = 0; break;
        case >= (byte)0: x4 = 0; acc += x4; r2 = 0; break;
        default: var a7 = new Acc6(5); acc += Look8(in a7);
    }
    acc += r2;
    return 0;
}
static int Look8(in Acc6 x) => x.Sum() + x.Total;
public sealed class Acc6(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
}
