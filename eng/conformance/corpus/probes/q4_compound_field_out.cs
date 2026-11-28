WriteLine(F(0));
static int F(int k)
{
    S s = new S(k);
    s.A += Set(out s.A);
    return s.A;
}
static int Set(out int p)
{
    p = 5;
    return 1;
}
public struct S(int a)
{
    public int A = a;
}
