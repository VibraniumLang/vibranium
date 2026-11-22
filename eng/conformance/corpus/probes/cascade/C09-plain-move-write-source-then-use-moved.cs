Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
Span<int> t = s;
WriteLine(t.Length);
b[0] = 1;
WriteLine(s.Length);
