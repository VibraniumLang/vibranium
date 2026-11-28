WriteLine(F(3));
static int F(int x)
{
    int r = 0;
    switch (x)
    {
        case int: r = 1; break;
        default: WriteLine(9);
    }
    return r;
}
