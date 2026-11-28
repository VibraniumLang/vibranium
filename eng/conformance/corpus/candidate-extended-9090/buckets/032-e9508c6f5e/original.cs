WriteLine(F(4, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var c7 = new C1(acc & (p0 - 5));
    acc += c7.Run();
    return acc;
}
public sealed class C1(int seed)
{
    public int F = seed;
    public int Run()
    {
        var sb3 = new StringBuf();
        sb3.Append("one\n\ntwo");
        sb3.Append(F);
        scoped ReadOnlySpan<byte> last4 = ""u8;
        foreach (var l2 in sb3.Lines()) { WriteLine(l2.SequenceEqual("b")); WriteLine(l2.Trim()); last4 = l2; }
        if (F < 2)
        {
            int x5 = F + 2;
        }
        ReadOnlySpan<int> sp6 = ((ushort)8 == (ushort)1) ? ([F, 9]) : ([4, F, 8]);
        WriteLine(sp6[0]);
        WriteLine(last4);
        F = F switch { < 1 when true => F, >= 1 => 7 };
        return F;
    }
}
