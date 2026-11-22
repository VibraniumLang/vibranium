WriteLine(F(0, true));
WriteLine(F(-3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    {
        if ((9 <= p0) && ((ushort)3 == (ushort)6))
        {
            var x5 = p1 switch { true => acc, false => p0 };
        }
        acc += Vw4(2);
        static int L1(int a)
        {
            if (a != 2)
            {
                ushort x2 = (ushort)(a - a);
            }
            return a;
        }
        acc += L1(acc);
    }
    return acc;
}
static ReadOnlySpan<int> Sx3(ReadOnlySpan<int> x, bool f) { if (f) { x = [7, 8]; } return x; }
static int Vw4(int p) { Vec<int> a = [p, 5, 6]; var s = Sx3(a.AsReadOnlySpan(), p > 0); return s[0] + s.Length; }
