ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x).Length);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = []; return p; }
