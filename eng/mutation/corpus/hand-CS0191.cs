static int F(int p0)
{
    if (p0 > 100)
    {
        SO s;
        s.A = 1;
    }
    return p0;
}
public struct SO(int a, int b, Vec<int> n)
{
    readonly public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
