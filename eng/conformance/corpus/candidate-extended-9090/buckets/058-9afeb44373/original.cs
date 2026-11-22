WriteLine(C.M(4, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Span<int> v2 = [acc, 9, p0];
        foreach (ref var e1 in v2)
        {
            e1 += 1;
            var a4 = new Acc3(v2.Length % (e1 % 3));
            var q6 = new Ctr5();
            q6.Inc();
            q6.Inc();
            acc += q6.Get();
            acc += a4.Sum();
            acc += a4.AddTwice(4);
            acc += p0;
            a4.Add(a4.Total);
            acc += a4.Total + a4.Items.Count;
            acc += e1;
        }
        return acc;
    }
}
public sealed class Acc3(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
}
public struct Ctr5
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
