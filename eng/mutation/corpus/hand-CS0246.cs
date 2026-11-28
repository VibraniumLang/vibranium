static int F()
{
    Vec<int> v = [1, 2, 3];
    Span<int> s = v.AsSpan();
    foreach (ref varZz e in s) 
    return v[0] + v.Count;
}
