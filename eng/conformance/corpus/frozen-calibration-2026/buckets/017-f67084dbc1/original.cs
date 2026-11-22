WriteLine(F(4, true));
WriteLine(F(1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    C1 c2 = new C1();
    acc += c2.Run();
    WriteLine(checked(acc + 1));
    if ((p0 is not 5) || (8.25 == 6.25)) VibraniumRuntime.Panic("boom");
    return acc;
}
public sealed class C1
{
    public int F = 2;
    public int Run()
    {
        WriteLine(-2L % 5L);
        int.TryParse("40", out F);
        return F;
    }
}
