WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v4 = [18, -4, -3, 2];
    Span<int> s3 = v4[..];
    Bump6(s3, 3);
    acc += M8(ref acc, 4) + (false ? 0 : 0);
    return acc;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int M8(ref int p, int k)
{
    p += k;
    return 0;
}
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
}
public sealed class Hold9(Acc1 inner)
{
    public Acc1 In = inner;
}
