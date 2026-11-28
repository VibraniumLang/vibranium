WriteLine(F(1, 4));
static int F(int x, int y)
{
    return x + y switch { 4 => 10, _ => 20 };
}
