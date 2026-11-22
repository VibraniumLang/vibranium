int k = 3;
Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = b.AsSpan();
int n = 0;
if (k > 1)
{
    n = (t = a.AsSpan()).Length;
    t = a.AsSpan();
}
else
{
    b[0] = 7;
}
WriteLine(n + t[0]);
