WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    WriteLine(checked(acc + 1));
    return 0;
}
public sealed class C1
{
    public int F = 0;
    public int Run()
    {
        int.TryParse("40", out F);
        return 0;
    }
}
