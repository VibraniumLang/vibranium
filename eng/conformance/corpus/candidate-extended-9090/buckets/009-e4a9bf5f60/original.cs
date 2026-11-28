WriteLine(F(2, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    var sb1 = new StringBuf(4);
    sb1.Append(false);
    sb1.Append(true);
    sb1.AppendRune(0xE9);
    sb1.AppendHex(0, 2);
    acc += acc - ((int)(ushort)7);
    var s2 = p0 switch { < 3 when 8 >= 5 => 1, >= 3 => 8 };
    acc += s2;
    WriteLine(sb1.AsUtf8());
    WriteLine(sb1.Length);
    return acc;
}
