WriteLine(C.M(0, true, -2L));
WriteLine(C.M(-2, false, 5L));
public static class C
{
    public static int M(int p0, bool p1, long p2)
    {
        int acc = p0;
        S s5;
        s5.B = 7;
        s5.A = p0;
        acc += acc;
        acc += Sum6(s5);
        acc += Snd4(p0);
        acc += Snd2(p0 + p0);
        acc += p0 - ((int)p2);
        return acc;
    }
    public static int Snd2(int p) { int acc = 0; Option<Cell1> o = Some(new Cell1(p)); var s = o.AsMut(); for (int i = 0; i < 2; i++) { acc += (s.IsSome ? 1 : 0); if (i == 0) { var w = o; acc += w.IsSome ? 1 : 0; } } return acc; }
    public static int Snd4(int p) { int acc = 0; var h = new Hold3(p); ReadOnlySpan<int> s = h.View(); for (int i = 0; i < p % 3; i++) { h = new Hold3(1); } acc += s.Length; return acc; }
    public static int Sum6(in S x) => x.A + x.B;
}
public sealed class Cell1(int v) { public int V = v; }
public sealed class Hold3(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }
public struct S
{
    public int A;
    public int B;
}
