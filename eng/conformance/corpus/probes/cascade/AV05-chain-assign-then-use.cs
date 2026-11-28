Vec<int> a = [1];
Vec<int> b = [2];
a = b = MakeVec(7);
WriteLine(a.Count);
WriteLine(b.Count);

static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }
