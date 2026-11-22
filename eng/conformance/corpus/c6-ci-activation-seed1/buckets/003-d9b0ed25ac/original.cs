WriteLine(F(2, true, -2L));
WriteLine(F(-2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    acc += (acc + 7) % 5;
    var s3 = p2 switch { 5L => p0, not 5L when p0 != 1 => p0, _ => 5 };
    acc += s3;
    acc += Vw2(p0);
    if (!(p2 != 3L))
    {
        if (((double)p0) != 5.25)
        {
            int x4 = ((int)7.25) / acc;
        }
        else
        {
            int x5 = s3;
        }
    }
    else
    {
        var x6 = p2 + (long)6;
    }
    return acc;
}
static ReadOnlySpan<int> Sx1(ReadOnlySpan<int> x) { x = [1, 2]; return x; }
static int Vw2(int p) { Vec<int> a = [p, 5, 6]; var s = Sx1(a.AsReadOnlySpan()); return s[0] + s.Length; }
