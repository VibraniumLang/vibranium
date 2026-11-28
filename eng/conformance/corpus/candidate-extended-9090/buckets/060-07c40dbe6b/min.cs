WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v4 = [13, 15, 0, -2, 10, 19];
        Span<int> s3 = v4[0..6];
        foreach (ref var t5_e0 in s3) t5_e0 += 3;
        ReadOnlySpan<int> t5_ro2 = s3;
        return acc;
    }
    public static int Eat2(Res x)
    {
        using var g = x;
        return x.Id;
    }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
