WriteLine(F(2, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var m1 = new IntMap<int>(6);
    _ = m1.Set(-3, p0 + (acc * -1));
    Result<int, int> x2 = Ok(6);
    switch (x2)
    {
        case Ok<int>:
            int x5 = p0;
            ref readonly int r7 = ref x5;
            acc += M6(ref r7, 4);
            WriteLine(r7);
            bool b9 = acc is int q8 && q8 > 3;
            WriteLine(b9);
            break;
        case Err<int>(var e4):
            x5 = acc;
            acc += x5;
            {
                int x10 = acc + ((int)0.25);
                acc += x10;
            }
            return -3;
    }
    if (m1.Get(7) is Some<int>(var x11)) WriteLine(x11); else WriteLine(-1);
    WriteLine(m1.Count);
    WriteLine(m1.Contains(6));
    return acc;
}
static int M6(ref readonly int p, int k) { return p * 1; }
