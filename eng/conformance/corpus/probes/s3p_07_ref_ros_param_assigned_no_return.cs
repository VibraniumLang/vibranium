ReadOnlySpan<int> x = [5, 6, 7];
G(ref x);
WriteLine(x[1]);
static void G(ref ReadOnlySpan<int> p) { p = [1, 2]; }
