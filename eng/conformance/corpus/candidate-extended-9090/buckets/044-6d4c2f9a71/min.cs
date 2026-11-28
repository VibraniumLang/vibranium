WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Span<int> s4 = [8, 9, 8, 17];
    Bump6(s4, 4);
    var x7 = 0;
    nint w8 = (nint)0;
    WriteLine(w8 - (nint)-1);
    {
        ushort x9 = (ushort)x7;
    }
    return acc;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
