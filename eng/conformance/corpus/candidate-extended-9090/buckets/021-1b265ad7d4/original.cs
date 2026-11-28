WriteLine(C.M(0, true));
WriteLine(C.M(-2, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Vec<byte> vb2 = [(byte)' ', (byte)'1', (byte)'2', 10, (byte)'-', (byte)'3', (byte)' ', 10, (byte)'x'];
        foreach (var l1 in vb2.AsReadOnlySpan().Lines()) { WriteLine(l1.StartsWith("x")); WriteLine(l1); WriteLine(l1.StartsWith("k")); }
        int x4 = p0 + 8;
        var s5 = p1 switch { true when true => acc, false => -3 };
        acc += s5;
        return acc;
    }
}
