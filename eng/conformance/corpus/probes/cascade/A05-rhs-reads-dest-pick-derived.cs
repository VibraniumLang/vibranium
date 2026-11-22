Vec<int> b = [3, 4, 5];
Span<int> t = b.AsSpan();
Span<int> s = t[1..];
t = Pick(t, s);
WriteLine(t[0]);

static Span<int> Pick(Span<int> x, Span<int> y) => x.Length > y.Length ? x : y;
