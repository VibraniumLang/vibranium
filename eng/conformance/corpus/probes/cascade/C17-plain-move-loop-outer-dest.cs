Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
for (int i = 0; i < 2; i++)
{
    Span<int> s = b.AsSpan();
    t = s;
    WriteLine(t.Length);
}
