Vec<int> v = [1, 2];
Span<int> sp = v.AsSpan();
int n = (v = MakeVec(5)).Count;
WriteLine(n + sp[0]);

static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
