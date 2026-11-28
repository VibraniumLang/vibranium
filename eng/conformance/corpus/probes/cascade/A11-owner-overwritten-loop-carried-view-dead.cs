Vec<int> v = [1, 2];
Span<int> sp = v.AsSpan();
for (int i = 0; i < 2; i++)
{
    WriteLine(sp[0]);
    v = MakeVec(i);
    sp = v.AsSpan();
}

static Vec<int> MakeVec(int a) { Vec<int> r = [a, a + 1]; return r; }
