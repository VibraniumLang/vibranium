WriteLine(F(2, true, -2L));
WriteLine(F(7, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Result<int, int> x1 = Err(acc * -2);
    int r2;
    switch (x1)
    {
        case Ok<int>(var v3): var x5 = acc + (int)0.25; r2 = 9; break;
        case Err<int>: r2 = p0 << ((int)2.25); break;
        default:
            Vec<int> v7 = [4, -3, acc];
            foreach (ref var e6 in v7)
            {
                e6 += 1;
                acc += p0;
                acc += e6;
            }
            var x8 = p0;
            break;
    }
    acc += r2;
    acc += r2;
    return acc;
}
