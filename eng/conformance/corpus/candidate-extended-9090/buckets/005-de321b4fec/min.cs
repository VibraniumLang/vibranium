WriteLine(F(0, false));
static int F(int p0, bool p1)
{
    int acc = 0;
    ReadOnlySpan<byte> tx1 = "xyz"u8;
    WriteLine(tx1.LastIndexOf((byte)'a'));
    acc += M5(ref acc, 0);
    return 0;
}
static int M5(in int p, int k) { return 0; }
