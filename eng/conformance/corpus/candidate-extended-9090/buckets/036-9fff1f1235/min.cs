WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        var a2 = new Acc1(acc);
        S s3;
        var s4 = 0 switch { < 0 when true => 0, >= 0 => 0 };
        acc += Sum5(s3);
        ReadOnlySpan<int> a2_r = a2.Items.AsReadOnlySpan();
        a2.Add(1);
        acc += a2_r.Length;
        return 0;
    }
    public static int Sum5(in S x) => 0;
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
}
public struct S
{
}
