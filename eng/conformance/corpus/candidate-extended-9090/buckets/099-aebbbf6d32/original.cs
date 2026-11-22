WriteLine(C.M(-1, true));
WriteLine(C.M(8, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        using Res r1 = new Res(7);
        Vec<int> v4 = [2, 20, 2, 3, 4, 17];
        Span<int> s3 = v4.AsSpan();
        foreach (ref var t5_e0 in s3) t5_e0 += 2;
        Bump6(s3[0..], 4);
        acc += s3[0];
        acc += SumRo7(s3);
        ushort x8 = (ushort)(p0);
        acc += v4.Count + v4[0];
        acc += Eat2(r1);
        return acc;
    }
    public static int Eat2(Res x)
    {
        using var _ = x;
        return x.Id;
    }
    public static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
    public static int SumRo7(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
