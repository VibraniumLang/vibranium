WriteLine(F(0));
static int F(int a)
{
    a += Set(out a);
    return a;
}
static int Set(out int p)
{
    p = 5;
    return 1;
}
