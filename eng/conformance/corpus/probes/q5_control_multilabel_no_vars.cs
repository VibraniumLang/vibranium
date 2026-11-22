WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case 1 when false:
        case 3: return 1;
        default: return 0;
    }
}
