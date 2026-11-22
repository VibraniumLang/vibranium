ReadOnlySpan<int> x = [5, 6, 7];
WriteLine(G(x)[0]);
static ReadOnlySpan<int> G(ReadOnlySpan<int> p) { p = [1, 2, 3]; return p[1..]; }
