Vec<int> b = [1, 2, 3];
Span<int> t = b.AsSpan();
WriteLine(t.Length);
Span<int> s = b.AsSpan();
t = s;
WriteLine(t.Length);
