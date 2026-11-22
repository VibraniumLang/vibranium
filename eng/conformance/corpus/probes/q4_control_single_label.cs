Result<int, int> r = Err(7);
WriteLine(F(r));
static int F(Result<int, int> x)
{
    switch (x)
    {
        case Err<int>(var e): return e;
        default: return 0;
    }
}
