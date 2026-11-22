WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    using Res r1 = new Res(0);
    Vec<int> v4 = [20, 0, -4, 8, 10, -2];
    Span<int> s3 = v4[0..6];
    foreach (ref var t5_e0 in s3) t5_e0 += 2;
    Span<int> t5_1 = s3[..^1];
    acc += Eat2(r1);
    return 0;
}
static int Eat2(Res x)
{
    using var _ = x;
    return x.Id;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
