WriteLine(F(0, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Vec<int> v2 = [7, 16, 11, 5];
    Span<int> s1 = v2.AsSpan();
    foreach (ref var t3_e0 in s1) t3_e0 += 1;
    acc += Pair4(s1[..0], s1[0..]);
    ReadOnlySpan<int> t3_r2 = s1.Slice(1, 2);
    acc += t3_r2.Length + (t3_r2.IsEmpty ? 0 : t3_r2[^1]);
    Option<Cell5> o6 = Some(new Cell5(s1.Length & acc));
    if (o6.AsMut() is Some<Cell5>(var c7)) c7.V += 5;
    if (o6.AsMut() is Some<Cell5>(var c8)) c8.Next = Some(new Cell5(4));
    if (o6.AsRef() is Some<Cell5>(var c8_a) && c8_a.Next.AsRef() is Some<Cell5>(var c8_n)) WriteLine(c8_n.V);
    if (o6.AsMut() is Some<Cell5>(var c9)) c9.Next = Some(new Cell5(9));
    if (o6.AsRef() is Some<Cell5>(var c9_a) && c9_a.Next.AsRef() is Some<Cell5>(var c9_n)) WriteLine(c9_n.V);
    OptionMut<Cell5> mm11 = o6.AsMut();
    int x12 = (acc | acc) << acc;
    mm11.Set(Some(new Cell5(4)));
    if (o6.AsRef() is Some<Cell5>(var o6_f)) WriteLine(o6_f.V); else WriteLine(-1);
    acc += v2.Count + v2[0];
    acc += acc;
    return acc;
}
static int Pair4(Span<int> a, Span<int> b) { if (a.Length > 0 && b.Length > 0) a[0] += b[0]; return a.Length + b.Length; }
public sealed class Cell5(int v)
{
    public int V = v;
    public Option<Cell5> Next = None;
}
