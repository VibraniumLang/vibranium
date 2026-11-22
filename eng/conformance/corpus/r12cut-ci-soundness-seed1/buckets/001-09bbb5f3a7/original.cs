WriteLine(F(3, true, -2L));
WriteLine(F(-2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += Snd2((p0 - -1) ^ -2);
    static int L3(int a) => (a * a) % a;
    int x5 = p0;
    int s8 = Find6(x5) switch { Some<int>(_) => (p1 ? x5 : acc) - acc, NoneType => acc };
    acc += s8;
    acc += L3(p0);
    acc += L4(acc);
    static int L4(int a) => a;
    return acc;
}
static int Sink1(Vec<int> x) => x.Count;
static int Snd2(int p) { int acc = 0; Vec<int> v = [p, 2, 3]; Span<int> s = v[0..2]; for (int i = 0; i < 2; i++) { acc += s[0]; if (i == 0) { acc += Sink1(Move(v)); } } return acc; }
static Option<int> Find6(int k) => k > 1 ? (Option<int>)Some(k * 4) : None;
