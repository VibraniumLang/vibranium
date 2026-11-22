WriteLine(F(-2, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [-1, 10, -1];
    Span<int> s1 = v2.Slice(0, 3);
    ReadOnlySpan<int> t3_ro0 = s1;
    acc += t3_ro0[0] + t3_ro0.Length;
    s1[0] = 5;
    ReadOnlySpan<int> t3_ro1 = s1;
    acc += t3_ro1[0] + t3_ro1.Length;
    s1[0] = 1;
    Span<int> t3_2 = s1[0..2];
    Span<int> s4 = [8, 9, 8, 17];
    ReadOnlySpan<int> t5_r0 = s4.Slice(1, 1);
    acc += t5_r0.Length + (t5_r0.IsEmpty ? 0 : t5_r0[^1]);
    Bump6(s4, 4);
    acc += s4[0];
    acc += s4[^1] + s4[^3];
    ReadOnlySpan<int> t5_r3 = s4.Slice(2, 1);
    acc += t5_r3.Length + (t5_r3.IsEmpty ? 0 : t5_r3[^1]);
    var x7 = acc + 1;
    acc += t3_2.Length;
    ReadOnlySpan<int> t3_ro3 = s1;
    acc += t3_ro3[0] + t3_ro3.Length;
    s1[0] = 5;
    acc += v2.Count + v2[0];
    nint w8 = (nint)(x7 + 5);
    WriteLine(w8 - (nint)-1);
    if ((acc is >= 5) || (-2.25 == -1.25))
    {
        ushort x9 = (ushort)(x7 ^ acc);
    }
    return acc;
}
static void Bump6(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
