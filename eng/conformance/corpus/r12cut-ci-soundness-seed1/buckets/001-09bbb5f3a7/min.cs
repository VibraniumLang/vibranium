WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Snd2((p0 - -1) ^ -2);
    static int L3(int a) => 0;
    int x5 = 0;
    int s8 = Find6(0) switch { Some<int>(_) => x5, NoneType => 0 };
    acc += L3(0);
    acc += L4(0);
    static int L4(int a) => 0;
    return 0;
}
static int Sink1(Vec<int> x) => x.Count;
static int Snd2(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v[0..2]; for (int i = 0; i < 2; i++) { acc += s[0]; if (i == 0) { acc += Sink1(Move(v)); } } return acc; }
static Option<int> Find6(int k) => (Option<int>)Some(0);
