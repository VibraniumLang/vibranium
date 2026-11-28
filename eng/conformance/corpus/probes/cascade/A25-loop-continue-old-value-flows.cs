Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = b.AsSpan();
for (int i = 0; i < 3; i++)
{
    WriteLine(t[0]);
    b[0] = i;
    if (i == 0) continue;
    t = a.AsSpan();
}
