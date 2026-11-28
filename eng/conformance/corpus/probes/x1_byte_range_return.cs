WriteLine(F(3));
WriteLine(F(200));
static int F(byte x)
{
    switch (x)
    {
        case < 100: return 1;
        case >= 100: return 2;
    }
}
