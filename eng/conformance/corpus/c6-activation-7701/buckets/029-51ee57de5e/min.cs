WriteLine(0);
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        using var r1 = new Res(0);
        acc += Eat2(r1);
        return 0;
    }
    public static int Eat2(Res x)
    {
        using var g = x;
        return x.Id;
    }
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = []; return x; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(0);
}
