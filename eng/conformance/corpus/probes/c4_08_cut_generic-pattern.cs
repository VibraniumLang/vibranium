WriteLine(F(5));
static int F<T>(T x)
{
    switch (x)
    {
        case 5: return 1;
        default: return 0;
    }
}
