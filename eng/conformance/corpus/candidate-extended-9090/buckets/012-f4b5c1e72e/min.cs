WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [14, -3, 3, -1, 0];
    Span<int> s1 = v2[..];
    Bump4(s1, 1);
    acc += M5(ref acc, 0);
    return 0;
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int M5(in int p, int k) { return 0; }
