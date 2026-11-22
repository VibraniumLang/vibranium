WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    Vec<int> v2 = [0, 19, 2];
    Span<int> s1 = v2.Slice(0, 3);
    foreach (ref var t3_e0 in s1) t3_e0 += 2;
    ReadOnlySpan<int> t3_r3 = s1.Slice(2, 0);
    var sb5 = new StringBuf();
    foreach (var l4 in sb5.Lines())
    sb5.Append("z");
    return acc;
}
