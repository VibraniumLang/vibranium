WriteLine(0);
public static class C
{
    public static int Snd2(int p) { int acc = 0; var h = new Hold1(p); ReadOnlySpan<int> s = h.View(); for (int i = 0; i < p % 3; i++) { var w = h; acc += w.K; } acc += s.Length; return acc; }
}
public sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }
