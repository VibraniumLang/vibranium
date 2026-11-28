int k = 3;
Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = b.AsSpan();
Span<int> s = a.AsSpan();
if (k > 1)
{
    t = s;
}
else
{
    b[0] = 7;
}
WriteLine(t[0]);
