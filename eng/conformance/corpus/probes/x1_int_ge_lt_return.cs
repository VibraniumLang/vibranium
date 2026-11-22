WriteLine(F(3));
WriteLine(F(-3));
static int F(int x)
{
    switch (x)
    {
        case >= 0: return 1;
        case < 0: return 2;
    }
}
