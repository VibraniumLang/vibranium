Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
s = Pick(s, b.AsSpan());
WriteLine(s[0]);

static Span<int> Pick(Span<int> x, Span<int> y) => x.Length > y.Length ? x : y;
