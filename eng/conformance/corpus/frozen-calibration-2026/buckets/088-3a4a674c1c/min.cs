WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    if (acc > 1000) throw new InvalidOperationException();
    return 0;
}
public sealed class C1(int seed)
{
    public int F = seed;
    public int Run()
    {
        int.TryParse("55", out F);
        return 0;
    }
}
