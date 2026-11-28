Span<int> x = [5, 6, 7];
WriteLine(G(x)[1]);
static Span<int> G(Span<int> p) { p = [1, 2]; return p; }
