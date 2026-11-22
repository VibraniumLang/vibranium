WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    SN n2;
    n2.C = 0;
    ref int r4 = ref acc;
    acc += M3(ref r4, 0);
    WriteLine(r4);
    return 0;
}
static int M3(in int p, int k) { return 0; }
public struct SN
{
    public int C;
}
