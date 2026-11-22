WriteLine(C.M(1, true));
WriteLine(C.M(4, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Span<int> s1 = [-4, 9, 19, 4, -4];
        acc += SumRo3(s1);
        foreach (ref var t2_e1 in s1) t2_e1 += 2;
        s1[4] += acc;
        acc += s1[4];
        Vec<int> v5 = [0, 9, -1, -5, -5, 4];
        Span<int> s4 = v5[0..6];
        foreach (ref var t6_e0 in s4) t6_e0 += 3;
        Span<int> t6_1 = s4[0..];
        var r7 = new Res(1);
        if (p0 <= 1)
        {
            int x9 = acc + 2;
        }
        acc += Eat8(r7);
        if (t6_1.Length > 0) t6_1[0] = acc;
        foreach (var t6_e1 in t6_1) acc += t6_e1;
        Span<int> t6_2 = s4[0..];
        acc += t6_2.Length;
        acc += v5.Count + v5[0];
        return acc;
    }
    public static int SumRo3(ReadOnlySpan<int> q) { int z = 0; foreach (var e in q) z += e; return z; }
    public static int Eat8(Res x)
    {
        using var g = x;
        return x.Id;
    }
}
public sealed class Res(int id) : IDisposable
{
    public int Id = id;
    public void Dispose() => WriteLine(Id + 100);
}
