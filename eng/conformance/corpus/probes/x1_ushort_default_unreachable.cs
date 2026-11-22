WriteLine(F(3));
static int F(ushort x)
{
    int r = 0;
    switch (x)
    {
        case >= (ushort)0: r = 1; break;
        default: WriteLine(2);
    }
    return r;
}
