WriteLine(C.M(6, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = 0;
        Option<Cell1> o2 = Some(new Cell1(p0));
        acc += o2.AsRef() switch { Some<Cell1>(var c3) => c3.V, _ => -1 };
        S s5;
        acc += Sum6(s5);
        acc += M7(out acc, 0) + (false ? 0 : 0);
        return acc;
    }
    public static int Sum6(in S x) => 0;
    public static int M7(out int p, int k)
    {
        p = 0;
        return 0;
    }
}
public sealed class Cell1(int v)
{
    public int V = v;
}
public struct S
{
}
