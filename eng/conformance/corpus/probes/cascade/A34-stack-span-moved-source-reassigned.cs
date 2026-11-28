Span<int> s = [1, 2, 3];
Span<int> t = s;
s = [4, 5];
WriteLine(t[0] + s[0]);
