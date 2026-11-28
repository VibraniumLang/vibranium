WriteLine(F(1, true));
WriteLine(F(4, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    ReadOnlySpan<byte> u2 = "\r\n\r\n"u8;
    scoped ReadOnlySpan<byte> last3 = ""u8;
    foreach (var l1 in u2.Lines())
    {
        WriteLine(l1);
        if (int.TryParse(l1.Trim(), out var pv4)) WriteLine(pv4);
        WriteLine(l1.SequenceEqual(""));
        last3 = l1;
        Option<int> u6 = Find5(-2);
        acc += acc;
        int s7 = (6.25 % 5.25) / (1.25 % -1.25) switch { -2.25 when p1 => p0, 0.25 when false => u6.UnwrapOr(6), _ => p0 };
        acc += s7;
        WriteLine(u6.IsEmpty);
    }
    WriteLine(last3);
    return acc;
}
static Option<int> Find5(int k) => k < 4 ? Some(k * 2) : None;
