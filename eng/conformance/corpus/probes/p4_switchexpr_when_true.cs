WriteLine(F(3));
static int F(int a)
{
    int s = a switch { < 0 => 0, >= 0 when true => 1 };
    return s;
}
