WriteLine(F(5, true));
WriteLine(F(8, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<byte> vb2 = [13, 10, 13, 10];
    foreach (var l1 in vb2.AsReadOnlySpan().Lines()) { acc += l1.Length; }
    ushort x4 = (ushort)(acc);
    var s5 = p0 switch { < 2 when true => acc, >= 2 => x4 < (ushort)3 ? 7 : p0 };
    acc += s5;
    acc += p0;
    return acc;
}
