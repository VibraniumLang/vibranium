Vec<int> b = [1, 2, 3];
Span<int> s = b.AsSpan();
Span<int> t = s;
WriteLine(s.Length);
WriteLine(t.Length);
