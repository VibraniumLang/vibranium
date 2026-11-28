Vec<Vec<int>> outer = [];
Vec<int> first = [1, 2];
outer.Add(first);
Span<int> sp = outer[0].AsSpan();
Vec<int> x = [4, 5];
outer[0] = x;
WriteLine(sp[0]);
