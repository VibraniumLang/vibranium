WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v4 = [2, 20, 2, 3, 4, 17];
        Span<int> s3 = v4.AsSpan();
        foreach (ref var t5_e0 in s3) t5_e0 += 2;
        acc += SumRo7(s3);
        return 0;
    }
    public static int Eat2(Res x)
    {
        using var _ = x;
        return x.Id;
    }
    public static int SumRo7(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
