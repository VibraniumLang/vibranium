Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> s = a.AsSpan();
WriteLine(Len(s = b.AsSpan()));
WriteLine(s.Length);

static int Len(Span<int> x) => x.Length;
