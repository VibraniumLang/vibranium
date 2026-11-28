WriteLine(F());
static int F()
{
    int r = 0;
    switch (E.B)
    {
        case E.B: r = 1; break;
        default: SO s; s.A = 1; WriteLine(s.A); r = 2; break;
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
