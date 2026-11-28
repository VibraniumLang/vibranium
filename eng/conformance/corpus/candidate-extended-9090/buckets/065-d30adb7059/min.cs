WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var r1 = new Res(0);
    Vec<int> v4 = [9, 15, 17, 17, 6];
    Span<int> s3 = v4[..];
    foreach (ref var t5_e0 in s3) t5_e0 += 3;
    Bump6(s3[0..4], 2);
    int x7 = 0;
    ulong w8 = (ulong)x7;
    acc += Eat2(r1);
    return 0;
}
static int Eat2(Res x)
{
    using var _ = x;
    return x.Id;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
