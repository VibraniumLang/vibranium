WriteLine(F(2, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    int x3 = ((int)(byte)2) % acc;
    WriteLine(unchecked((nint)float.NaN));
    acc += Vw2(6.25 switch { 2.25 => 1, _ => acc });
    return acc;
}
static ReadOnlySpan<int> Pick1(ReadOnlySpan<int> x, ReadOnlySpan<int> y) => x;
static int Vw2(int p) { Vec<int> a = [p, 2]; Vec<int> b = [3, 4]; var s = Pick1(a.AsReadOnlySpan(), b.AsReadOnlySpan()); return s[0]; }
