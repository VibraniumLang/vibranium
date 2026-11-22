WriteLine(F(1, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    ushort x1 = (ushort)(p0);
    switch (x1)
    {
        case < (ushort)4 when false:
            var m2 = new IntMap<int>(3);
            _ = m2.Set(3, 8);
            m2.Set(9, 8L <= 3L ? (int)'a' : acc);
            m2.Set(5, -2);
            SNO n4;
            n4.In.A = p0 switch { 7 when acc != 1 => 2, > 2 => p0, _ => acc };
            n4.In.B = -3;
            n4.In.V = [5];
            n4.C = p0;
            acc = p0 switch { < 1 => acc, >= 1 => p0 };
            acc += n4.In.A + n4.C;
            acc += m2.Get(4).UnwrapOr(-1);
            WriteLine(m2.Count);
            WriteLine(m2.Contains(3));
            break;
        case >= (ushort)4: break;
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
