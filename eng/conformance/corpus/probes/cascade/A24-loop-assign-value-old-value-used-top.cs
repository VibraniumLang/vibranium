Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Vec<int> c = [6];
Span<int> s = a.AsSpan();
Span<int> t = c.AsSpan();
for (int i = 0; i < 2; i++)
{
    b[0] = i;
    WriteLine(t[0]);
    t = (s = b.AsSpan());
}
