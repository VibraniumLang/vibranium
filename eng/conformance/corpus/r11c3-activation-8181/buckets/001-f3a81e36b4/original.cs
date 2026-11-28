WriteLine(F(4, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var b2 = p1;
    int w3 = (int)(acc + 9);
    WriteLine(w3 % 6);
    acc += Ca1(acc);
    return acc;
}
static int Ca1(int p) { Vec<int> x = [p, 2]; int r = Eat(in x, x); return r; static int Eat(in Vec<int> a, Vec<int> b) { int k = Sink(b); return (a.Count > 0 ? a[0] : -1) + k; } static int Sink(Vec<int> x) => (x.Count > 0 ? x[0] : -1); }
