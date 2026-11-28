Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
int n = 0;
for (int i = 0; i < 2; i++)
{
    b[0] = i;
    n += s[0];
    n += (s = b.AsSpan()).Length;
}
WriteLine(n);
