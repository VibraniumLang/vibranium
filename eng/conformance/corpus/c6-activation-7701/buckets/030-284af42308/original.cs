// vib-conformance: file-name Program.g.cs
WriteLine(C.M(0, true));
WriteLine(C.M(-3, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += Vw2(p0 - 8);
        var c6 = new C3(5);
        acc += c6.Run();
        return acc;
    }
    public static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
    public static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
}
public sealed class C3(int seed)
{
    public int F = seed;
    public int Run()
    {
        if (((double)9L) != ((double)F))
        {
            ushort x4 = (ushort)(F);
        }
        else
        {
            var x5 = F * F;
        }
        int.TryParse("44", out F);
        return F;
    }
}
