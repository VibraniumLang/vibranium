WriteLine(F(6, true, -2L));
WriteLine(F(0, false, 5L));
static int F(int p0, bool p1, long p2)
{
    int acc = p0;
    Vec<int> v2 = [15, 2, 8, 13, -3, 14];
    Span<int> s1 = v2.Slice(0, 6);
    Bump4(s1, 1);
    acc += s1[0];
    acc += s1[^1] + s1[^4];
    Option<Cell5> o6 = Some(new Cell5(2));
    OptionMut<Cell5> mm8 = o6.AsMut();
    var c7 = mm8.Replace(Some(new Cell5(7)));
    if (o6.AsRef() is Some<Cell5>(var c9)) acc += c9.V;
    if (o6.AsMut() is Some<Cell5>(var c10)) c10.Next = Some(new Cell5(2));
    if (o6.AsRef() is Some<Cell5>(var c10_a) && c10_a.Next.AsRef() is Some<Cell5>(var c10_n)) WriteLine(c10_n.V);
    var c11 = o6.Take();
    WriteLine(c11.IsSome);
    WriteLine((uint)p2);
    if (o6.AsRef() is Some<Cell5>(var o6_f)) WriteLine(o6_f.V); else WriteLine(-1);
    var s12 = p0 switch { < 6 => acc, >= 6 when true => (int)(p1 ? 7.25 : 4.25) };
    acc += s12;
    acc += Over13(s1[0..2], s1[1..3]);
    acc += v2.Count + v2[0];
    return acc;
}
static void Bump4(Span<int> q, int d) { for (int i = 0; i < q.Length; i++) q[i] += d; }
static int Over13(Span<int> a, Span<int> b) => a.Length + b.Length;
public sealed class Cell5(int v)
{
    public int V = v;
    public Option<Cell5> Next = None;
}
