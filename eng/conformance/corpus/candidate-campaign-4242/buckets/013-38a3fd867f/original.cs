WriteLine(C.M(-2, true));
WriteLine(C.M(-3, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        acc += acc;
        acc += P1(acc is 2 ? None : Some(acc), -1);
        var s2 = p0 switch { < 4 => (int)((long)p0), >= 4 when 5.25 >= 4.25 => P1(Some(p0), p0) };
        acc += s2;
        return acc;
    }
    public static int P1(Option<int> c0, int c1) => c0.UnwrapOr(-1) + c1;
}
