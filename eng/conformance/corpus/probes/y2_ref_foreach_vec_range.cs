WriteLine(F());
static int F()
{
    Vec<int> v = [1, 2, 3];
    Span<int> s = v[0..3];
    foreach (ref var e in s) e += 1;
    return v[0];
}
