WriteLine(F(1));
static int F(int x)
{
    return x is > 0 or <= 0 ? 1 : 2;
}
