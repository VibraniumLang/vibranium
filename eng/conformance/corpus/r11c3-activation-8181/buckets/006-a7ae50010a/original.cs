if (File.AppendAllText("/dev/stderr", "appended\n").IsErr(out _, out var e2)) WriteLine(e2.Error.KindName()); else WriteLine("ok");
WriteLine(C.M(4, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        var c4 = new C1((int)((float)5L));
        acc += c4.Run();
        int x5 = p0;
        return acc;
    }
}
public sealed class C1(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        long x3 = (long)F;
        int.TryParse("59", out F);
        return F;
    }
}
