WriteLine(F(4, true, -2L));
WriteLine(F(3, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var sm1 = new StrMap<int>(8);
    _ = sm1.Set("key"u8, 0 / acc);
    _ = sm1.Set("a\n", acc);
    int x2 = p0;
    S s4 = new S();
    acc += M3(ref s4, 3);
    acc += sm1.Get("key").UnwrapOr(-1);
    WriteLine(sm1.Count);
    if (p1 && (p2 < 1L)) VibraniumRuntime.Panic("stop");
    return acc;
}
static int M3(in S p, int k) { return p.A * 3; }
public struct S
{
    public int A;
    public int B;
}
