Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
int n = 0;
for (int i = 0; i < 2; i++)
{
    n += t[0];
    Span<int> s = b.AsSpan();
    t = s;
    a.Add(i);
}
WriteLine(n + t.Length);
