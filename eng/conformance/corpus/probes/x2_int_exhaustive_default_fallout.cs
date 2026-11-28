WriteLine(F(3));
WriteLine(F(-3));
static int F(int x)
{
    int r = 0;
    switch (x)
    {
        case >= 0: r = 1; break;
        case < 0: r = 2; break;
        default: WriteLine(9);
    }
    return r;
}
