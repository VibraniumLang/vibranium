WriteLine(C.M(5, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        var a2 = new Acc1(acc);
        var q4 = new Ctr3();
        q4.Inc();
        q4.Inc();
        acc += q4.Get();
        acc += 9 - p0;
        acc += M5(ref acc, 2) + (p1 && p1 ? acc : acc);
        WriteLine(acc);
        foreach (var a2_e1 in a2.Items) WriteLine(a2_e1);
        acc += a2.Total + a2.Items.Count;
        return acc;
    }
    public static int M5(ref int p, int k)
    {
        p += k;
        return p * 2;
    }
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
}
public struct Ctr3
{
    public int N;
    public void Inc() { N += 1; }
    public readonly int Get() => N;
}
