Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
Span<int> t = s;
s = b.AsSpan();
WriteLine(s.Length);
WriteLine(t.Length);
