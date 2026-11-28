Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
Span<int> t = a.AsSpan();
WriteLine(t[0]);
t = Id(b.AsSpan());
a.Add(7);
WriteLine(t[0]);
static Span<int> Id(Span<int> x) => x;
