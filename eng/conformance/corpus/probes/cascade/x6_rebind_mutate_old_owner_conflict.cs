Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
Span<int> u = t[..1];
t = b.AsSpan();
a.Add(7);
WriteLine(u[0] + t[0]);
