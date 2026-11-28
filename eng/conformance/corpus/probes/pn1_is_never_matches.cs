WriteLine(F(1));
static int F(int x)
{
    return x is > 0 and < 0 ? 1 : 2;
}
