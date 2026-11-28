Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
Span<int> t = (s = b.AsSpan());
WriteLine(t.Length);
WriteLine(s.Length);
