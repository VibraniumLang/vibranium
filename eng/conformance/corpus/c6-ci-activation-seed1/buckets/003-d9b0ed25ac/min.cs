WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    var s3 = 0;
    acc += Vw2(p0);
    {
        int x5 = s3;
    }
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
