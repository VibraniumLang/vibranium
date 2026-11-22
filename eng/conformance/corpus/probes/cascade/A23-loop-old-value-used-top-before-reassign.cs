Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
for (int i = 0; i < 2; i++)
{
    b[0] = i;
    WriteLine(t[0]);
    Span<int> s = b.AsSpan();
    t = s;
}
