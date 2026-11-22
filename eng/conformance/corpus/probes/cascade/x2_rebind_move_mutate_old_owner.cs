Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
WriteLine(t[0]);
Span<int> s = b.AsSpan();
t = s;
a.Add(7);
WriteLine(t[0]);
