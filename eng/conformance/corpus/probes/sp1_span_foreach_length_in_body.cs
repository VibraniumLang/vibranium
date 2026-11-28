WriteLine(F());
static int F()
{
    Span<int> s = [1, 2];
    int n = 0;
    foreach (var e in s)
    {
        n += e + s.Length;
    }
    return n;
}
