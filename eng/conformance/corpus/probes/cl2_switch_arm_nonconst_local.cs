WriteLine(F(1));
static int F(int x)
{
    int k = 1;
    return x switch { k => 1, _ => 2 };
}
