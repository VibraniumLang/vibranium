WriteLine(F(0, true));
static int F(int a, bool c)
{
    a += c ? Set(out a) : 0;
    return a;
}
static int Set(out int p)
{
    p = 5;
    return 1;
}
