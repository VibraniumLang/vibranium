WriteLine(C.M(-2, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        var a2 = new Acc1(acc);
        acc += a2.AddTwice(2);
        acc += a2.AddTwice(4);
        S s3 = new S();
        WriteLine(-2 >> (p1 ? p0 : p0));
        var s4 = p0 switch { < 4 when true => p0 % 6, >= 4 => acc };
        acc += s4;
        s3.B = acc & 6;
        acc += Sum5(s3);
        ReadOnlySpan<int> a2_r = a2.Items.AsReadOnlySpan();
        a2.Add(1);
        acc += a2_r.Length;
        acc += a2.Total + a2.Items.Count;
        acc += p0;
        return acc;
    }
    public static int Sum5(in S x) => x.A + x.B;
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
public struct S
{
    public int A;
    public int B;
}
