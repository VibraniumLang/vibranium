WriteLine(F(-1, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    using Res r1 = new Res(3);
    Vec<int> v4 = [20, 0, -4, 8, 10, -2];
    Span<int> s3 = v4[0..6];
    foreach (ref var t5_e0 in s3) t5_e0 += 2;
    Span<int> t5_1 = s3[..^1];
    acc += 5 + acc;
    if (t5_1.Length > 0) t5_1[0] = p0 * 0;
    acc += t5_1.Length;
    acc += v4.Count + v4[0];
    acc += Eat2(r1);
    acc = (-2 * p0) >> acc;
    return acc;
}
static int Eat2(Res x)
{
    using var _ = x;
    return x.Id;
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
