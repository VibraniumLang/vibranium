WriteLine(F(1, true, -2L));
WriteLine(F(2, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1(p0));
    acc += o2.AsRef() switch { Some<Cell1>(var c3) => c3.V, _ => -1 };
    var c4 = o2.Replace(Some(new Cell1(4)));
    if (c4.AsRef() is Some<Cell1>(var c4_p)) WriteLine(c4_p.V);
    var c5 = o2.Replace(Some(new Cell1(8)));
    if (c5.AsRef() is Some<Cell1>(var c5_p)) WriteLine(c5_p.V);
    Vec<int> v7 = [12, 9, 6, 3, 9];
    Span<int> s6 = v7.AsSpan();
    acc += s6[^1] + s6[^4];
    foreach (ref var t8_e1 in s6) t8_e1 += 2;
    ReadOnlySpan<int> t8_r2 = s6.Slice(1, 3);
    acc += t8_r2.Length + (t8_r2.IsEmpty ? 0 : t8_r2[^1]);
    s6[3] = -3;
    acc += s6[3];
    WriteLine(p0);
    acc += v7.Count + v7[0];
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    int s9 = acc switch { < 3 when true => (int)(ushort)0, >= 3 => p0 };
    acc += s9;
    return acc;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
