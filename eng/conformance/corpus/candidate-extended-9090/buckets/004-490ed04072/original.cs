WriteLine(F(2, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> s1 = [15, 2, 18, -3];
    foreach (ref var t2_e0 in s1) t2_e0 += 3;
    ReadOnlySpan<int> t2_r1 = s1.Slice(3);
    acc += t2_r1.Length + (t2_r1.IsEmpty ? 0 : t2_r1[^1]);
    foreach (ref var t2_e2 in s1) t2_e2 += 1;
    acc += Pair3(s1[..4], s1[4..]);
    using (var r4 = new Res(3))
    {
        WriteLine(r4.Id);
        acc += acc;
    }
    return acc;
}
static int Pair3(Span<int> a, Span<int> b) { if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
