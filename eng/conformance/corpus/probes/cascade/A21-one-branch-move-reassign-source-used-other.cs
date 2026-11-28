int k = 3;
Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
if (k > 1)
{
    Span<int> u = s;
    WriteLine(u[0]);
    s = a.AsSpan();
}
else
{
    b[0] = 7;
}
WriteLine(s[0]);
