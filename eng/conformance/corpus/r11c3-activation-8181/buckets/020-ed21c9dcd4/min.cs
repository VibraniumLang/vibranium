WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    acc += Ct4(acc);
    return 0;
}
static Vec<int> MakeV2() => [1, 2, 3];
static int Len3(ReadOnlySpan<int> s) => s.Length > 0 ? s[0] + s.Length : 0;
static int Ct4(int p) { bool c = p > 0; return Len3(c ? MakeV2().AsReadOnlySpan() : MakeV2().AsReadOnlySpan()[..1]); }
file static class Hidden1 { public static int K() => 7; }
