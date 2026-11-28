Vec<int> a = [1, 2]; Span<int> s = a.AsSpan(); WriteLine(Len(s = [])); WriteLine(s.Length); static int Len(Span<int> x) => x.Length;
