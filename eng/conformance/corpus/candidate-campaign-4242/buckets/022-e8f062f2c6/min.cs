WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    var ok1 = false;
    WriteLine(ok1);
    ref int r3 = ref acc;
    acc += M2(ref r3, 0);
    WriteLine(r3);
    return 0;
}
static int M2(in int p, int k) { return 0; }
