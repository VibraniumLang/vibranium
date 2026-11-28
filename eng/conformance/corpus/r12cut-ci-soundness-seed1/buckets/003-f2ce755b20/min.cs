WriteLine(0);
public static class C
{
    public static int Snd2(int p) { int acc = 0; Option<Cell1> o = Some(new Cell1(p)); var s = o.AsMut(); for (int i = 0; i < 2; i++) { acc += (s.IsSome ? 1 : 0); if (i == 0) { var w = o; acc += w.IsSome ? 1 : 0; } } return acc; }
}
public sealed class Cell1(int v) { public int V = v; }
