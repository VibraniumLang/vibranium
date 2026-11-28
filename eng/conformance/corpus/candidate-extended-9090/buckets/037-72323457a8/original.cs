WriteLine(F(5, true, -2L));
WriteLine(F(-2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    C1 c5 = new C1(acc switch { < 4 when true => acc, >= 4 when true => p0 });
    acc += c5.Run();
    int x6 = p0 + -1;
    return acc;
}
public sealed class C1(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        var sb2 = new StringBuf(4);
        sb2.Truncate(sb2.Length > 0 ? sb2.Length - 1 : 0);
        sb2.Append(7);
        sb2.AppendLine("a");
        if ((-1.25 + 0.25) != ((double)F))
        {
            var x3 = F;
        }
        else
        {
            var x4 = (int)((char)(4 ^ F));
        }
        WriteLine(sb2.AsUtf8());
        WriteLine(sb2.Length);
        int.TryParse("53", out F);
        return F;
    }
}
