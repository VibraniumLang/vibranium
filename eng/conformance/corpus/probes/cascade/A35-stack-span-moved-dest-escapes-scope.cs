Span<int> t = [0];
{
    Span<int> s = [1, 2, 3];
    t = s;
}
WriteLine(t[0]);
