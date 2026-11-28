WriteLine(F(2, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p0 is int q1)
    {
        acc += q1;
        if (q1 is int q2)
        {
            acc += q2;
            acc %= 2;
        }
        WriteLine(q2);
    }
    WriteLine(q1);
    Vec<int> v4 = [9, 4];
    acc += M3(ref v4[0], 3);
    ushort x5 = (ushort)(acc);
    return acc;
}
static int M3(ref readonly int p, int k) { return p * 1; }
