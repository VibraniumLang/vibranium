WriteLine(F(7, 4));
static int F(int x, int y)
{
    return x % y switch { 4 => 1, _ => 2 };
}
