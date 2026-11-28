WriteLine(C.M(0, true));
WriteLine(C.M(7, false));
public static class C
{
    public static int M(int p0, bool p1)
    {
        int acc = p0;
        Result<int, int> x3 = Ok(6 + p0);
        switch (x3)
        {
            default: break;
            case Err<int>(var e5) when e5 < 0: VibraniumRuntime.Panic("boom"); break;
            case Ok<int>(var v4): acc += v4; break;
        }
        acc += Snd2(!p1 ? p0 - p0 : acc);
        return acc;
    }
    public static int Snd2(int p) { int acc = 0; var h = new Hold1(p); ReadOnlySpan<int> s = h.View(); for (int i = 0; i < p % 3; i++) { var w = h; acc += w.K; } acc += s.Length; return acc; }
}
public sealed class Hold1(int p) { public Vec<int> Items = [p, 2, 3]; public int K = p; public ReadOnlySpan<int> View() => Items.AsReadOnlySpan(); [Mut] public void Push(int k) => Items.Add(k); }
