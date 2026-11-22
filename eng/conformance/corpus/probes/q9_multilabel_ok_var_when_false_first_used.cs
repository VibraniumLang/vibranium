Result<int, int> r = Ok(4);
WriteLine(F(r));
static int F(Result<int, int> x)
{
    switch (x)
    {
        case Err<int>(var e) when false:
        case Ok<int>(var v): return v;
        default: return 0;
    }
}
