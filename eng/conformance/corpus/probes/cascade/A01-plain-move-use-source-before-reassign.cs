Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
Span<int> t = s;
WriteLine(s.Length);
s = a.AsSpan();
WriteLine(t[0] + s[0]);
