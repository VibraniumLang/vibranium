Vec<int> v = [1];
int n = (v = MakeVec(7)).Count;
v = MakeVec(n);
WriteLine(v[0]);

static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }
