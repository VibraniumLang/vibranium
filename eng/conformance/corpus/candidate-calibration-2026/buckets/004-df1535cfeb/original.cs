WriteLine(F(-2, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (p0 is var q1 && q1 < 4)
    {
        acc += q1;
        acc += q1 switch { < 5 => q1, >= 5 when true => 1 };
    }
    acc += Fail2(p0);
    return acc;
}
static int Fail2(int k)
{
    if (k > 1) VibraniumRuntime.Panic("stop");
    return k;
}
