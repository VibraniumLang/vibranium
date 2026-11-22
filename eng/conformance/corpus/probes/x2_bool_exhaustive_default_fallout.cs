WriteLine(F(true));
WriteLine(F(false));
static int F(bool b)
{
    int r = 0;
    switch (b)
    {
        case true: r = 1; break;
        case false: r = 2; break;
        default: WriteLine(9);
    }
    return r;
}
