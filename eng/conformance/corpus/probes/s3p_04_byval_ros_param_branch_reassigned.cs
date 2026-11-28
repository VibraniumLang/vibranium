ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x, 2)[1]);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k)
{
    if (k > 1)
    {
        p = [1, 2];
    }
    return p;
}
