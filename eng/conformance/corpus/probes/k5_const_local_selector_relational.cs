WriteLine(F());
static int F()
{
    const int k = 7;
    int r = 0;
    switch (k)
    {
        case < 0: SO s; s.A = 1; WriteLine(s.A); r = 1; break;
        case >= 0: r = 2; break;
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
