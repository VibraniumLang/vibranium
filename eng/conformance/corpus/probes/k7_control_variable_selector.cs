WriteLine(F(E.C));
static int F(E e)
{
    int r = 0;
    switch (e)
    {
        case E.A: SO s; s.A = 1; WriteLine(s.A); r = 1; break;
        default: r = 2; break;
    }
    return r;
}
public enum E { A = 1, B = 2, C = 7 }
public struct SO(int a, int b, Vec<int> n)
{
    public int A = a;
    public int B = b;
    public Vec<int> V = n;
}
