Vec<int> v = [1, 2];
Vec<int> other = [3];
Span<int> sp = v.AsSpan();
v = other;
WriteLine(sp[0]);
