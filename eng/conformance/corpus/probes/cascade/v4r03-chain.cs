Vec<int> a = [1, 2]; Vec<int> b = [3]; Span<int> s = a.AsSpan(); Span<int> t = a.AsSpan(); t = s = b.AsSpan(); WriteLine(t.Length); WriteLine(s.Length);
