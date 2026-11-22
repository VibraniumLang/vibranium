WriteLine(F(1, true));
WriteLine(F(-1, false));
static int F(int p0, bool p1)
{
    int acc = p0;
    var a2 = new Acc1((int)((long)-1L));
    acc += a2.Sum();
    ReadOnlySpan<byte> u4 = "a\r\nb\r\n"u8;
    foreach (var l3 in u4.Lines())
    {
        if (int.TryParse(l3.Trim(), out var pv6)) WriteLine(pv6);
        Vec<int> v8 = [2, 9, 16, 5, -3];
        Span<int> s7 = v8.Slice(0, 5);
        foreach (ref var t9_e0 in s7) t9_e0 += 3;
        acc += s7[^1] + s7[^3];
        Span<int> t9_2 = s7[1..];
        acc -= p0;
        acc += t9_2.Length;
        acc += Over10(s7, s7);
        acc += v8.Count + v8[0];
    }
    var l3_lines = "a\r\nb\r\n".Lines();
    foreach (var l3_z in l3_lines) WriteLine(l3_z);
    foreach (var a2_e1 in a2.Items) WriteLine(a2_e1);
    a2.Add(a2.Total);
    acc += a2.Total + a2.Items.Count;
    return acc;
}
static int Over10(Span<int> a, Span<int> b) => a.Length + b.Length;
public sealed class Acc1(int seed)
{
    public int Total = seed;
    public Vec<int> Items = [];
    [Mut] public void Add(int x) { Total += x; Items.Add(x); }
    public int Sum() { int t = 0; foreach (var e in Items) t += e; return t; }
    [Mut] public int AddTwice(int x) { Add(x); Add(x); return Total; }
    [Mut] public int Drain() { int c = Items.Count; Items.Clear(); return c; }
}
