WriteLine(F(4, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (Find1(p0 % 6).IsNone(out var v2)) return acc;
    acc += v2;
    WriteLine((uint)(p1 ? 8L : -1L));
    S s5 = new S();
    acc += M4(ref s5, 2);
    return acc;
}
static Option<int> Find1(int k) => k > 3 ? Some(k * 4) : None;
static int M4(in S p, int k) { return p.A * 3; }
public struct S
{
    public int A;
    public int B;
}
