WriteLine(F(3, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    char x1 = (char)p0;
    int r2;
    switch (x1)
    {
        case < 'e':
            var a4 = new Acc3(p1 ? (int)5.25 : (int)-3.5f);
            a4.Add(a4.Total);
            var _ = p0;
            acc += _;
            _ = acc;
            WriteLine(_);
            foreach (var a4_e1 in a4.Items) WriteLine(a4_e1);
            acc += a4.Sum();
            a4.Add(a4.Total);
            acc += a4.Total + a4.Items.Count;
            r2 = 6 * (p1 ? _ : p0);
            break;
        case >= 'e': r2 = 6; break;
        default:
            {
                static int L5(int a) => a <= 0 ? a > 3 ? a : a : a + L5(a - 1);
                acc += L5(p0 << 8);
            }
            r2 = p0;
    }
    acc += r2;
    acc += p0 % (acc * p0);
    return acc;
}
public sealed class Acc3(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
