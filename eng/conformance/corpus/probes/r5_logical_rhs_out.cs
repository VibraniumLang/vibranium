WriteLine(F(0));
static int F(int a)
{
    bool b = true;
    b &= Set(out a) > 0 && a > 3;
    a += b ? 10 : 20;
    return a;
}
static int Set(out int p)
{
    p = 5;
    return 1;
}
