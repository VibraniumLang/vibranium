Vec<int> v = [1];
int n = (v = MakeVec(7)).Count;
WriteLine(n);
WriteLine(v.Count);

static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }
