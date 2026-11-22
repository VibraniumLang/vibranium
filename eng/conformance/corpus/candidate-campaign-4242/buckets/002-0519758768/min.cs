WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    var s2 = 0 switch { < 0 => 0, >= 0 when true => 0 };
    acc += M3(ref s2, 0);
    return 0;
}
static int M3(ref readonly int p, int k) { return 0; }
