Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan()).Length; WriteLine(n); WriteLine(s.Length);
