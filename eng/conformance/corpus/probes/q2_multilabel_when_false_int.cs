WriteLine(F(3));
static int F(int x)
{
    switch (x)
    {
        case 1 when false:
        case var n: return n;
    }
}
