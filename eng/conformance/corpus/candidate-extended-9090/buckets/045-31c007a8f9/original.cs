WriteLine(F(2, true));
WriteLine(F(2, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    foreach (var l1 in "a\r\nb\r\n".Lines()) { WriteLine(l1.StartsWith("k")); }
    Vec<int> v4 = [8, 6, 4, 0];
    Span<int> s3 = v4[0..4];
    Span<int> t5_0 = s3[2..];
    ushort x6 = (ushort)(6 & acc);
    acc += P7(p0 > 0 ? p0 : acc, acc);
    if (t5_0.Length > 0) t5_0[0] = p0;
    acc += t5_0.Length;
    s3[3] += p0 << p0;
    acc += s3[3];
    foreach (ref var t5_e2 in s3) t5_e2 += 3;
    Span<int> t5_3 = s3[2..];
    foreach (var t5_e3 in t5_3) acc += t5_e3;
    acc += v4.Count + v4[0];
    var l1_lines = "a\r\nb\r\n".Lines();
    foreach (var l1_z in l1_lines) WriteLine(l1_z);
    return acc;
}
static int P7(int c0, int c1) => c0 + c1;
