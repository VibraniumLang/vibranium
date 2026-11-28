WriteLine(F(-1, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [0, 19, 2];
    Span<int> s1 = v2.Slice(0, 3);
    foreach (ref var t3_e0 in s1) t3_e0 += 2;
    acc += s1[^1] + s1[^1];
    acc += s1[^1] + s1[^1];
    ReadOnlySpan<int> t3_r3 = s1.Slice(2, 0);
    acc += t3_r3.Length + (t3_r3.IsEmpty ? 0 : t3_r3[^1]);
    var sb5 = new StringBuf();
    sb5.Append("a\nb");
    sb5.AppendLine("\n");
    foreach (var l4 in sb5.Lines())
    {
        WriteLine(l4.StartsWith("k"));
        WriteLine(l4.IndexOf((byte)'2'));
        sb5.Append("z");
        long x7 = (long)acc;
    }
    acc += v2.Count + v2[0];
    return acc;
}
