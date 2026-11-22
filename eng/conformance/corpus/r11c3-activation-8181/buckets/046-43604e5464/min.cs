WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    acc += Cr3(p0);
    static int L4(int a) { return 0; }
    acc += L4(0);
    return 0;
}
static int Idx2(Vec<int> b) => b.Count - 1;
static int Cr3(int p) { Vec<int> x = [p, 2]; int r = x[Idx2(Move(x))]; return r; }
