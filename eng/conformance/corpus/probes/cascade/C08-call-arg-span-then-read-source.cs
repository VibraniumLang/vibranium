Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
WriteLine(Len(s));
WriteLine(b[0]);
WriteLine(s.Length);

static int Len(Span<int> x) => x.Length;
