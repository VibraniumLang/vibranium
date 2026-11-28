Vec<int> b = [3, 4, 5];
Span<int> s = b.AsSpan();
WriteLine(b[0]);
s = s[1..];
WriteLine(s[0]);
