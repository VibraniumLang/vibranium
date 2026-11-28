Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
int n = (s = b.AsSpan())[0];
WriteLine(n);
WriteLine(s.Length);
