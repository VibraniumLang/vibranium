WriteLine(F(3));
static int F(int a)
{
    switch (a)
    {
        case < 0:
            return 0;
        case >= 0 when true:
            return 1;
    }
}
