WriteLine(F(-2, true));
WriteLine(F(7, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    acc += Cr3(p0);
    WriteLine(p1);
    WriteLine(unchecked((nint)(123.999f)));
    WriteLine(unchecked((nint)(-1e20f)));
    static int L4(int a)
    {
        int x5 = a;
        return ((a < 1 ? a : a) & (true ? a : a)) + (int)E.C;
    }
    acc += L4(acc);
    return acc;
}
static int Idx2(Vec<int> b) => b.Count - 1;
static int Cr3(int p) { Vec<int> x = [p, 2]; int r = x[Idx2(Move(x))]; return r; }
public enum E { A = 0, B = 1, C = 6 }
