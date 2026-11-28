WriteLine(F(5, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Snd3(p0 << p0);
    acc += -1;
    Vec<int> v5 = [2, 3];
    acc += M4(in v5[0], 5);
    return acc;
}
static ReadOnlySpan<int> Head1(ReadOnlySpan<int> x) => x[..1];
static int Sink2(Vec<int> x) => x.Count;
static int Snd3(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; ReadOnlySpan<int> s = Head1(v.AsReadOnlySpan()); for (int i = 0; i < p % 3; i++) { acc += Sink2(v); } acc += s[0]; return acc; }
static int M4(in int p, int k) { return p * 2; }
