WriteLine(F(6, true));
WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    if (acc is int q1 && q1 != 2)
    {
        acc += q1;
        var x2 = acc - -1;
        acc += First3(p1 ? ([q1, 2, q1]) : ([1, 8]));
    }
    WriteLine(q1);
    return acc;
}
static int First3(ReadOnlySpan<int> q) => q.Length > 0 ? q[0] : -1;
