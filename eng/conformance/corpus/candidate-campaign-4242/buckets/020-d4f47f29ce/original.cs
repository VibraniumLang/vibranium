WriteLine(F(2, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    S s1;
    s1.B = acc;
    s1.A = (p0 + acc) + acc;
    C2 c4 = new C2(8 + (6 / p0));
    acc += c4.Run();
    acc += s1.A + s1.B;
    var x5 = acc % p0;
    return acc;
}
public struct S
{
    public int A;
    public int B;
}
public sealed class C2(int seed)
{
    public int F = seed;
    [Mut] public int Run()
    {
        WriteLine(5L);
        var s3 = (1.5f * 5.5f) % (-1.5f - 0.5f) switch { -2.5f => F, 0.5f => F, -1.5f => -1, _ => (int)((byte)(3 + F)) };
        WriteLine(s3);
        int.TryParse("22", out F);
        return F;
    }
}
