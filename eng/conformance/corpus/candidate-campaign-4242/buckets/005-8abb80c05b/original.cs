WriteLine(F(4, true));
WriteLine(F(-2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [p0, 3];
    foreach (var e1 in v2)
    {
        if (acc is < 6)
        {
            double d3 = (double)p0;
        }
        else
        {
            var b4 = acc > v2.Length;
        }
        acc += e1;
    }
    var s5 = acc switch { <= 3 => acc, 5 => p0, _ => p0 };
    acc += s5;
    return acc;
}
