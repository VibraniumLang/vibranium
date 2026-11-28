WriteLine(C.M(5, true));
WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Res r1 = new Res(3);
        Vec<int> v4 = [13, 15, 0, -2, 10, 19];
        Span<int> s3 = v4[0..6];
        foreach (ref var t5_e0 in s3) t5_e0 += 3;
        s3[2] = acc;
        acc += s3[2];
        ReadOnlySpan<int> t5_ro2 = s3;
        acc += t5_ro2[0] + t5_ro2.Length;
        s3[0] = 8;
        WriteLine(((long)2L) % 5L);
        acc += v4.Count + v4[0];
        acc += Eat2(r1);
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
    public void Dispose() => WriteLine(Id + 100);
}
