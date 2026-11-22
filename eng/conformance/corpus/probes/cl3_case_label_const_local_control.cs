WriteLine(F(1));
static int F(int x)
{
    const int k = 1;
    switch (x)
    {
        case k: return 1;
        default: return 2;
    }
}
