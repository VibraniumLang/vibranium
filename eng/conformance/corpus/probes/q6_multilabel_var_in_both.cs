WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case < 0 when false:
        case int n when n > 1: return n;
        default: return 0;
    }
}
