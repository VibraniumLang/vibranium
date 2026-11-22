WriteLine(F(2, true));
WriteLine(F(5, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (!p1)
    {
        var x1 = p0 + 1;
    }
    int s2 = acc switch { < 3 when !p1 => 9, >= 3 => p0 ^ -3, _ => acc };
    acc += s2;
    int[] arr = [s2, 2];
    WriteLine(arr[0]);
    return acc;
}
