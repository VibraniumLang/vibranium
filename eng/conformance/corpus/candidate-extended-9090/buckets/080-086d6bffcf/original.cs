WriteLine(F(3, true));
WriteLine(F(3, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    Option<Cell1> o2 = Some(new Cell1((acc / 7) & acc));
    if (o2.AsMut() is Some<Cell1>(var c3)) c3.Next = Some(new Cell1(2));
    if (o2.AsRef() is Some<Cell1>(var c3_a) && c3_a.Next.AsRef() is Some<Cell1>(var c3_n)) WriteLine(c3_n.V);
    var c4 = o2.Take();
    WriteLine(c4.IsSome);
    Span<int> v6 = [acc, 5, acc];
    foreach (var e5 in v6)
    {
        S s7;
        s7.A = -3;
        s7.B = p1 && p1 ? acc : acc;
        var t8 = s7;
        acc += p1 ? -2 : acc + acc;
        t8.A = v6.Length;
        acc += t8.A + t8.B;
        if ((ushort)3 > (ushort)2) continue;
        acc += e5;
    }
    if (o2 is Some<Cell1>(var x9)) x9.V = 2;
    if (o2.AsRef() is Some<Cell1>(var o2_f)) WriteLine(o2_f.V); else WriteLine(-1);
    return acc;
}
public sealed class Cell1(int v)
{
    public int V = v;
    public Option<Cell1> Next = None;
}
public struct S
{
    public int A;
    public int B;
}
