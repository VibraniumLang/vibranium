WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    const ushort K1 = (ushort)0;
    Vec<int> v3 = [0, 0];
    foreach (ref var e2 in v3)
    {
        e2 += 1;
        acc += (int)(char)(0 - e2);
    }
    static int L4(int a) => a <= 0 ? 0 : a + L4(a - 1) + (int)K1;
    acc += L4(acc);
    return 0;
}
