Vec<int> a = [1, 2];
Vec<int> b = [3, 4, 5];
ReadOnlySpan<int> t = a.AsReadOnlySpan();
WriteLine(t[0]);
t = b.AsReadOnlySpan();
a.Add(7);
WriteLine(t[0]);
