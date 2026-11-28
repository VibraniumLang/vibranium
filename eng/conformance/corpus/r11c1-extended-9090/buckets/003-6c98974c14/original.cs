WriteLine(F(3, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    byte x1 = (byte)(acc >> -1);
    int r2;
    switch (x1)
    {
        case < (byte)3:
            int x4 = acc switch { var n3 when n3 < 1 => acc, _ => acc };
            r2 = p0 % 1;
            break;
        case >= (byte)3:
            x4 = (int)((byte)p0);
            acc += x4;
            int.TryParse("5", out var o5);
            acc += o5;
            r2 = ((int)4.25) >> (o5 ^ -2);
            break;
        default:
            var a7 = new Acc6(5);
            acc += Look8(in a7);
            acc += p0;
            acc += a7.Sum();
            foreach (var a7_e2 in a7.Items) WriteLine(a7_e2);
            acc += a7.Total + a7.Items.Count;
            break;
    }
    acc += r2;
    return acc;
}
static int Look8(in Acc6 x) => x.Sum() + x.Total;
public sealed class Acc6(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
