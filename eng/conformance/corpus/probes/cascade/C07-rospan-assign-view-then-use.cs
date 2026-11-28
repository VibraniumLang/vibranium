Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
ReadOnlySpan<int> r = a.AsSpan();
int n = (r = b.AsSpan()).Length;
WriteLine(n);
WriteLine(r.Length);
