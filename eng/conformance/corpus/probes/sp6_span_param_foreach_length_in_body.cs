Span<int> t = [1, 2];
WriteLine(G(t));
static int G(Span<int> s)
{
    int n = 0;
    foreach (var e in s)
    {
        n += e + s.Length;
    }
    return n;
}
