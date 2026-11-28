WriteLine(F(1));
static int F(int p0)
{
    return p0;
    SO s;
    s.A = 1;
    s.B = 2;
    s.V = [1];
    return s.A;
}
public struct SO(int a, int b, Vec<int> n)
{
    public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
public struct SNO
{
    public SO In;
    public int C;
}
