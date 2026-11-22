WriteLine(F(5, true, -2L));
WriteLine(F(5, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var sb2 = new StringBuf();
    sb2.Append("tail\r");
    sb2.AppendLine("");
    sb2.Append(p0 / -3);
    foreach (var l1 in sb2.Lines()) { if (int.TryParse(l1.Trim(), out var pv4)) WriteLine(pv4); if (int.TryParse(l1.Trim(), out var pv5)) WriteLine(pv5); }
    Vec<int> v7 = [3, 2, 13, 19, 8];
    Span<int> s6 = v7.AsSpan();
    ReadOnlySpan<int> t8_ro0 = s6;
    acc += t8_ro0[0] + t8_ro0.Length;
    s6[0] = 8;
    Bump9(s6[3..^2], 3);
    acc += s6[0];
    ushort x10 = (ushort)(p0 % 6);
    acc += v7.Count + v7[0];
    var s11 = (5.5f + 0.5f) + -3.5f switch { 1.5f => p0, 2.5f => p0, > 2.5f => p0, _ => 8.5f switch { _ => p0 } };
    acc += s11;
    if (!p1)
    {
        int x12 = acc;
    }
    else
    {
        if (((ushort)p2) >= x10)
        {
            ushort x13 = x10;
        }
    }
    return acc;
}
static void Bump9(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
