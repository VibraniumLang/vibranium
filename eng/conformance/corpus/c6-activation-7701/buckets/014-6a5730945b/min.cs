WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    bool ok5 = TryGet4(0, out _);
    bool ok6 = false;
    WriteLine(ok6);
    acc += Vw2(2);
    return 0;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s.Length + a.Count; }
static bool TryGet4(int k, out int v)
{
    v = 0;
    return false;
}
