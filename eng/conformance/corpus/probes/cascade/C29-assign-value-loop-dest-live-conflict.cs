Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
Span<int> t = a.AsSpan();
for (int i = 0; i < 2; i++)
{
    t = (s = b.AsSpan());
    b[0] = i;
    WriteLine(t[0]);
}
