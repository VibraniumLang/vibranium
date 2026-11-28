WriteLine(F(1));
static int F(int p0)
{
    int acc = p0;
    switch (p0)
    {
        case < 4:
            SNO n4;
            n4.In.A = 1;
            n4.In.B = -3;
            n4.In.V = [5];
            n4.C = p0;
            acc += n4.In.A + n4.C;
            break;
        default: break;
    }
    return acc;
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
