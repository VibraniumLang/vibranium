WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Vec<int> v5 = [0, 9, -1, -5, -5, 4];
        Span<int> s4 = v5[0..6];
        foreach (ref var t6_e0 in s4) t6_e0 += 3;
        Span<int> t6_2 = s4[0..];
        return acc;
    }
    public static int Eat8(Res x)
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
