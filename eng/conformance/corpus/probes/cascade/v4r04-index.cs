Vec<int> a = [1, 2]; Vec<int> b = [3, 4]; Span<int> s = a.AsSpan(); int n = (s = b.AsSpan())[0]; WriteLine(n + s[1]);
