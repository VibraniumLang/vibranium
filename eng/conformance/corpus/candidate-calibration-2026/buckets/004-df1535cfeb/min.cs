WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += 0 switch { < 0 => 0, >= 0 when true => 0 };
    acc += Fail2(0);
    return 0;
}
static int Fail2(int k) { return 0; }
