Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
Span<int> t = s;
WriteLine(t.Length);
WriteLine(b[0]);
WriteLine(s.Length);
