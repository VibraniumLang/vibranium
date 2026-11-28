WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Result<int, int> x1 = Err(0);
    int r2;
    switch (x1)
    {
        case Ok<int>(var v3): r2 = 0; break;
        case Err<int>: r2 = 0; break;
    }
    acc += r2;
    return 0;
}
