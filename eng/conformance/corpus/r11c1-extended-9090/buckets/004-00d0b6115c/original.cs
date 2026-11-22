WriteLine(C.M(3, true, -2L));
WriteLine(C.M(-1, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        var sb1 = new StringBuf(2);
        sb1.AppendLine("");
        sb1.Append(2147483647);
        int r2;
        switch (p1)
        {
            case true: acc = 1; int w3 = (int)(p2 + 4); WriteLine(w3 % -3); r2 = w3 / p0; break;
            case false: r2 = acc; break;
            default: break;
        }
        acc += r2;
        WriteLine(sb1.AsUtf8());
        WriteLine(sb1.Length);
        WriteLine(0u);
        return acc;
    }
}
