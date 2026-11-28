Vec<int> b = [3, 4, 5];
for (int i = 0; i < 2; i++)
{
    Span<int> s = b.AsSpan();
    Span<int> u = s;
    WriteLine(u.Length);
}
