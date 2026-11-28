Vec<int> v = [1, 2];
for (int i = 0; i < 2; i++)
{
    Span<int> sp = v.AsSpan();
    v = MakeVec(i);
    WriteLine(sp[0]);
}

static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
