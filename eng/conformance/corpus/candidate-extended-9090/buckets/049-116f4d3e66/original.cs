WriteLine(C.M(0, true, -2L));
WriteLine(C.M(5, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        {
            using var r1 = new Res(3);
            WriteLine(r1.Id);
            var a3 = new Acc2((int)(ushort)6);
            foreach (var a3_e0 in a3.Items) WriteLine(a3_e0);
            acc += Look4(in a3);
            acc += p1 && p1 ? acc : p0;
            acc += M5(ref acc, 2);
            WriteLine(acc);
            a3.Add(p0);
            acc += a3.Total + a3.Items.Count;
        }
        return acc;
    }
    public static int Look4(in Acc2 x) => x.Sum() + x.Total;
    public static int M5(in int p, int k) { return p * 3; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
public sealed class Acc2(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
