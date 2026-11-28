WriteLine(F());
static int F()
{
    Vec<int> v = [1, 2, 3];
    Span<int> s = v.AsSpan();
    foreach (ref var e in s) e += 1;
    return v[0] + v.Count;
}
