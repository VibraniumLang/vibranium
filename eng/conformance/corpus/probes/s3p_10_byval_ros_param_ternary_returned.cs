ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x, 2)[1]);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p, int k) { ReadOnlySpan<int> q = [1, 2]; p = k > 1 ? q : p; return p; }
