Vec<int> v = [1];
WriteLine(Count(v = MakeVec(7)));
WriteLine(v.Count);

static int Count(Vec<int> x) => x.Count;
static Vec<int> MakeVec(int a) { Vec<int> v = [a, a + 1, a + 2]; return v; }
