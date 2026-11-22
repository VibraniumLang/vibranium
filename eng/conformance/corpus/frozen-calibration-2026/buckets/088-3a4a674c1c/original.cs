WriteLine(F(4, true));
WriteLine(F(6, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    C1 c2 = new C1((int)(2L >> p0));
    acc += c2.Run();
    if (acc > 1000) throw new InvalidOperationException();
    return acc;
}
public sealed class C1(int seed)
{
    public int F = seed;
    public int Run()
    {
        WriteLine(F is not 1);
        int.TryParse("55", out F);
        return F;
    }
}
