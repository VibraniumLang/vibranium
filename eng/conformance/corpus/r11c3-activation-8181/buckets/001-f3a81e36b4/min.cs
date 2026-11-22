WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    int w3 = 0;
    WriteLine(w3);
    acc += Ca1(acc);
    return 0;
}
static int Ca1(int p) { Vec<int> x = [p, 2]; int r = Eat(in x, x); return r; static int Eat(in Vec<int> a, Vec<int> b) { int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
