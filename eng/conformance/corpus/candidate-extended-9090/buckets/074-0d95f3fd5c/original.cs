WriteLine(F(4, true));
WriteLine(F(9, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var sb1 = new StringBuf(3);
    sb1.AppendLine("a");
    sb1.AppendLine("");
    sb1 += "";
    acc -= p0 | p0;
    WriteLine(sb1.AsUtf8());
    WriteLine(sb1.Length);
    acc += M2(out acc, 1) + (p1 ? acc : -1 | p0);
    WriteLine(acc);
    return acc;
}
static int M2(out int p, int k)
{
    p = k;
    return p * 2;
}
