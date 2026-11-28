ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x)[1]);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p) { return p = [1, 2]; }
