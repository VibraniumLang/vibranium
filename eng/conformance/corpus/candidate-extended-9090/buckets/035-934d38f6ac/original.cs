WriteLine(F(-1, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var sb2 = new StringBuf();
    sb2.Append("a\nb");
    foreach (var l1 in sb2.Lines()) { WriteLine(l1.StartsWith("x")); WriteLine(l1.IndexOf((byte)' ')); }
    bool b4 = (p1 && p1) || (p1 && p1);
    var s5 = 9.5f % 6.5f switch { 2.5f => p0, -1.5f => (p0 - acc) - (p1 ? 9 : -3), _ => p0 };
    acc += s5;
    return acc;
}
