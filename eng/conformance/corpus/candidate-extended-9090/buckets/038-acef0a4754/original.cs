WriteLine(F(0, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1(acc);
    foreach (var a2_e0 in a2.Items) WriteLine(a2_e0);
    acc += a2.Sum();
    Vec<int> v4 = [18, -4, -3, 2];
    Span<int> s3 = v4[..];
    Bump6(s3, 3);
    acc += s3[0];
    acc += s3[^1] + s3[^3];
    int x7 = p0;
    acc += M8(ref acc, 4) + (p1 ? p0 < 4 ? p0 : acc : acc);
    WriteLine(acc);
    acc += v4.Count + v4[0];
    var h10 = new Hold9(new Acc1(3));
    h10.Push(5);
    acc += h10.In.Total + h10.In.Sum();
    acc += a2.Total + a2.Items.Count;
    return acc;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int M8(ref int p, int k)
{
    p += k;
    return p * 3;
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
public sealed class Hold9(Acc1 inner)
{
    public Acc1 In = inner;
    [Mut] public void Push(int x) { In.Add(x); }
}
