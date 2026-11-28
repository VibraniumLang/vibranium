WriteLine(F(1L));
static int F(long a)
{
    int s = a switch { < 0L => 0, >= 0L when false => 1 };
    return s;
}
