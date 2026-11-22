ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x, x)[1]);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p, ReadOnlySpan<int> q) { q = p = [1, 2]; return q; }
