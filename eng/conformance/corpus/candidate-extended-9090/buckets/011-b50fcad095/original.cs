WriteLine(F(4, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [7];
    foreach (var e1 in v2)
    {
        Span<int> s3 = [9, 12, 6, 16, 4];
        foreach (ref var t4_e0 in s3) t4_e0 += 3;
        ReadOnlySpan<int> t4_r1 = s3.Slice(3, 0);
        acc += t4_r1.Length + (t4_r1.IsEmpty ? 0 : t4_r1[^1]);
        var a6 = new Acc5(-1);
        a6.Add(a6.Total);
        var q8 = new Ctr7();
        q8.Inc();
        q8.Inc();
        acc += q8.Get();
        acc += p0;
        acc += a6.Total + a6.Items.Count;
        acc += acc * ((int)(ushort)9);
        acc += e1;
    }
    return acc;
}
public sealed class Acc5(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
}
public struct Ctr7
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
