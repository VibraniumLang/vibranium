WriteLine(F(4));
static int F(int x)
{
    return -x switch { -4 => 1, _ => 2 };
}
