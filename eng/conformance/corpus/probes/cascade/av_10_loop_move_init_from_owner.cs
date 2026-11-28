Vec<int> b = [1, 2, 3];
Span<int> t = b.AsSpan();
for (int i = 0; i < 2; i++)
{
    Span<int> s = b.AsSpan();
    t = s;
    WriteLine(t.Length);
}
