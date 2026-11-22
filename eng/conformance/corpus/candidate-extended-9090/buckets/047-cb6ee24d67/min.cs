WriteLine(F(0, false, 0L));
static int F(int p0, bool p1, long p2)
{
    int acc = 0;
    Vec<int> v2 = [15, 2, 8, 13, -3, 14];
    Span<int> s1 = v2.Slice(0, 6);
    Bump4(s1, 1);
    var s12 = 0 switch { < 0 => 0, >= 0 when true => 0 };
    acc += Over13(s1[0..2], s1[1..3]);
    return 0;
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int Over13(Span<int> a, Span<int> b) => a.Length + b.Length;
