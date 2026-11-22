WriteLine(F(4, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (acc is var q1)
    {
        acc += q1;
        acc += Vw3(0 + q1);
        int x4 = p0;
    }
    return acc;
}
static ReadOnlySpan<int> Sx2(ReadOnlySpan<int> x) { x = []; return x; }
static int Vw3(int p) { Vec<int> a = [p, 5, 6]; var s = Sx2(a.AsReadOnlySpan()); return s.Length + a.Count; }
