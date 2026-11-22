WriteLine(F(1, 1L));
static int F(int k, long a)
{
    int s = k switch { _ => a switch { < 0L => 0, >= 0L when false => 1 } };
    return s;
}
