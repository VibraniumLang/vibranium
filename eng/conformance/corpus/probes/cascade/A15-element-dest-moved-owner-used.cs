Vec<Vec<int>> outer = [];
Vec<int> first = [1];
outer.Add(first);
Vec<int> x = [4, 5];
outer[0] = x;
WriteLine(x.Count);
