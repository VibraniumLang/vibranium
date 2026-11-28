WriteLine(F(3));
static int F(int x)
{
    return x switch { < 0 => 0, >= 0 when 1 > 0 => 1 };
}
