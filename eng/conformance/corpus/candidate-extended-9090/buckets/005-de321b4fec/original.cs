WriteLine(F(0, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    ReadOnlySpan<byte> tx1 = "xyz"u8;
    WriteLine(tx1.StartsWith("xy"));
    WriteLine(tx1.LastIndexOf((byte)'a'));
    if (long.TryParse("9223372036854775807", out var pv4)) WriteLine(pv4); else WriteLine(0);
    acc += acc;
    acc += M5(ref acc, 4);
    WriteLine(acc);
    return acc;
}
static int M5(in int p, int k) { return p * 1; }
