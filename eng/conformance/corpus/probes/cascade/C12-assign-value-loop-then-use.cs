Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
int n = 0;
for (int i = 0; i < 2; i++)
{
    n += (s = b.AsSpan()).Length;
}
WriteLine(n);
WriteLine(s.Length);
