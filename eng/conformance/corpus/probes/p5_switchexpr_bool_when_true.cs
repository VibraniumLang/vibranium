WriteLine(F(false));
static int F(bool b)
{
    int s = b switch { true => 0, false when true => 1 };
    return s;
}
