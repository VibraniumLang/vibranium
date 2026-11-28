Result<int, int> r = Err(7);
WriteLine(F(r));
static int F(Result<int, int> x)
{
    int acc = 0;
    switch (x)
    {
        case Ok<int>(var v) when false:
        case Err<int>(var e): acc += e; break;
    }
    return acc;
}
