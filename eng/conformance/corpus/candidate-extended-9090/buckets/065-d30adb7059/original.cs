WriteLine(F(2, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var r1 = new Res(6);
    Vec<int> v4 = [9, 15, 17, 17, 6];
    Span<int> s3 = v4[..];
    foreach (ref var t5_e0 in s3) t5_e0 += 3;
    s3[5] = p0;
    acc += s3[4];
    Bump6(s3[0..4], 2);
    acc += s3[0];
    foreach (ref var t5_e3 in s3) t5_e3 += 1;
    int x7 = (int)((ushort)p0);
    ulong w8 = (ulong)(x7 + 1);
    WriteLine(w8 >> 26);
    acc += v4.Count + v4[0];
    acc += Eat2(r1);
    int x9 = p0;
    return acc;
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
    public void Dispose() => WriteLine(Id + 100);
}
