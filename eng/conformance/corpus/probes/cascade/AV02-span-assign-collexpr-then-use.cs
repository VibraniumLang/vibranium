Vec<int> a = [1, 2];
Span<int> s = a.AsSpan();
int n = (s = []).Length;
WriteLine(n);
WriteLine(s.Length);
