WriteLine(C.M(0, true));
WriteLine(C.M(3, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        using var r1 = new Res(3);
        var x5 = (long)acc;
        acc += Vw4((acc - p0) / p0);
        acc += Eat2(r1);
        return acc;
    }
    public static int Eat2(Res x)
    {
        using var g = x;
        return x.Id;
    }
    public static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x) { x = []; return x; }
    public static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan()); return s.Length + a.Count; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
