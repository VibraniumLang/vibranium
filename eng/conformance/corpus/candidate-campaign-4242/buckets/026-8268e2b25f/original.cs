WriteLine(F(2, true, -2L));
WriteLine(F(6, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Option<int> u2 = Find1(acc);
    var c5 = new C3(4);
    acc += c5.Run();
    acc += p0 switch { < 5 when true => 9, >= 5 => p0 + p0 };
    return acc;
}
static Option<int> Find1(int k) => k > 0 ? Some(k * 2) : None;
public sealed class C3(int seed)
{
    public int F = seed;
    public int Run()
    {
        int x4 = F;
        int.TryParse("39", out F);
        return F;
    }
}
