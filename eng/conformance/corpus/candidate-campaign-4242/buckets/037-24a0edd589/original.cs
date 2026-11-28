WriteLine(F(0, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Span<int> v2 = [0];
    foreach (var e1 in v2)
    {
        acc += p0;
        if (e1 is > 2 and < 3)
        {
            var d3 = (double)acc;
        }
        acc += e1;
    }
    ReadOnlySpan<int> sp4 = [2, p0, acc];
    acc += sp4[0];
    return acc;
}
