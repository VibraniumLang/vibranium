WriteLine(F());
static int F()
{
    Vec<int> v = [1, 2, 3];
    Span<int> s = v.AsSpan();
    int t = 0;
    foreach (var e in s) t += e;
    return t + v[0];
}
