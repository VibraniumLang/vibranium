WriteLine(F(0));
static int F(int a)
{
    a += Bump(ref a);
    return a;
}
static int Bump(ref int p)
{
    p += 10;
    return 1;
}
