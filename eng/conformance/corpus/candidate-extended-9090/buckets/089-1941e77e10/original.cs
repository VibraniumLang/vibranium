WriteLine(C.M(3, true));
WriteLine(C.M(4, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        var sb2 = new StringBuf();
        sb2.Append("one\n\ntwo");
        sb2.AppendLine("x\ry");
        foreach (var l1 in sb2.Lines()) { WriteLine(l1.Trim()); WriteLine(l1.IndexOf((byte)'=')); }
        int x4 = (int)(p1 ? 5L : 7L);
        ref readonly int r6 = ref x4;
        acc += M5(ref r6, 2);
        WriteLine(r6);
        return acc;
    }
    public static int M5(ref readonly int p, int k) { return p * 1; }
}
