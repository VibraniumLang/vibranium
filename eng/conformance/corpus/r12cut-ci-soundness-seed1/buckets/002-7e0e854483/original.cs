WriteLine(C.M(3, true, -2L));
WriteLine(C.M(8, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        using (var r1 = new Res(2))
        {
            WriteLine(r1.Id);
            acc += Snd2(acc);
            acc += !p1 ? p0 - p0 : p0;
        }
        return acc;
    }
    public static int Snd2(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v.AsSpan(); for (int i = 0; i < 2; i++) { acc += s.Length; if (i == 0) { var w = v; acc += w.Count; } } return acc; }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
